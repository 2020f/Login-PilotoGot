using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Login.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Login.Tests;

// The real 8→10 token check is Verify-Net10AuthCompatibility.ps1 against a
// preserved QA database. These tests cover wire format and configuration
// invariants on .NET 10; they do not pretend that 10-issued cookies came from 8.
public sealed class Net10AuthCompatibilityTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public void Compatibility_suite_executes_on_the_target_runtime()
        => Assert.Equal(10, Environment.Version.Major);

    [Fact]
    public async Task Existing_session_JSON_contract_refreshes_once_and_preserves_the_pilot_role()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var refresh = $"{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("pilot-a"))}.{sessionId}.{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == "pilot-a");
            // Exact persisted property names and values used before the runtime
            // migration: Hash, ExpiresAt and Stamp in AspNetUserTokens.
            var legacyDocument = JsonSerializer.Serialize(new
            {
                Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresh))),
                ExpiresAt = app.Clock.GetUtcNow().UtcDateTime.AddDays(20), Stamp = user.SecurityStamp
            });
            db.UserTokens.Add(new IdentityUserToken<string>
                { UserId = user.Id, LoginProvider = "PilotGoMobile", Name = sessionId, Value = legacyDocument });
            await db.SaveChangesAsync();
        });
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var renewed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        var json = await renewed.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("token").GetString();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.NotEqual(refresh, json.GetProperty("refreshToken").GetString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("pilot-a", me.GetProperty("userId").GetString());
        Assert.Equal("Piloto", Assert.Single(me.GetProperty("roles").EnumerateArray()).GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/piloto/mis-ordenes")).StatusCode);
        var wrongRole = await client.GetAsync("/api/tienda/ordenes");
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
        Assert.Equal("application/json", wrongRole.Content.Headers.ContentType?.MediaType);
        Assert.Null(wrongRole.Headers.Location);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).StatusCode);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.UserTokens.Where(t => t.UserId == "pilot-a" && t.LoginProvider == "PilotGoMobile").ToListAsync());
            Assert.Empty(await db.OrdenesEntrega.ToListAsync());
        });
    }

    [Fact]
    public async Task Changed_roles_invalidate_existing_JWT_without_granting_the_new_role_to_the_old_token()
    {
        using var pilot = await app.LoginAsync("pilot-b");
        Assert.Equal(HttpStatusCode.OK, (await pilot.GetAsync("/api/auth/me")).StatusCode);
        await app.InScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByIdAsync("pilot-b"))!;
            Assert.True((await users.RemoveFromRoleAsync(user, "Piloto")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Cliente")).Succeeded);
        });
        foreach (var path in new[] { "/api/auth/me", "/api/piloto/mis-ordenes", "/api/tienda/ordenes" })
        {
            var denied = await pilot.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal("application/json", denied.Content.Headers.ContentType?.MediaType);
            Assert.Null(denied.Headers.Location);
        }
    }

    [Fact]
    public void Cookie_ticket_survives_provider_recreation_only_with_the_same_keys_application_and_purpose()
    {
        var repository = new MemoryKeyRepository();
        using var issuer = KeyProvider(repository, "Envirapido.RuntimeCompatibility");
        var issuedAt = DateTimeOffset.UtcNow;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "manager-a"), new Claim(ClaimTypes.Role, "Gestor")
        }, IdentityConstants.ApplicationScheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
            { IssuedUtc = issuedAt, ExpiresUtc = issuedAt.AddHours(1) }, IdentityConstants.ApplicationScheme);
        var protectedCookie = CookieFormat(issuer).Protect(ticket);
        using var receiver = KeyProvider(repository, "Envirapido.RuntimeCompatibility");
        var restored = CookieFormat(receiver).Unprotect(protectedCookie);
        Assert.NotNull(restored);
        Assert.Equal("manager-a", restored.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(restored.Principal.IsInRole("Gestor"));
        Assert.False(restored.Principal.IsInRole("SuperAdmin"));
        Assert.Equal(ticket.Properties.ExpiresUtc, restored.Properties.ExpiresUtc);
        using var differentApplication = KeyProvider(repository, "AnotherTenantOrApplication");
        Assert.Null(CookieFormat(differentApplication).Unprotect(protectedCookie));
        var wrongPurpose = new TicketDataFormat(receiver.GetRequiredService<IDataProtectionProvider>().CreateProtector("UnrelatedPurpose"));
        Assert.Null(wrongPurpose.Unprotect(protectedCookie));
        using var missingKeys = KeyProvider(new MemoryKeyRepository(), "Envirapido.RuntimeCompatibility");
        Assert.Null(CookieFormat(missingKeys).Unprotect(protectedCookie));
    }

    [Fact]
    public async Task Identity_token_survives_shared_key_provider_recreation_but_not_a_wrong_purpose_or_security_stamp()
    {
        var repository = new MemoryKeyRepository();
        using var issuer = KeyProvider(repository, "Envirapido.RuntimeCompatibility");
        using var receiver = KeyProvider(repository, "Envirapido.RuntimeCompatibility");
        await app.InScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByIdAsync("shop-a"))!;
            var passwordBefore = user.PasswordHash;
            var emitted = IdentityTokenProvider(issuer);
            var validation = IdentityTokenProvider(receiver);
            var token = await emitted.GenerateAsync("ResetPassword", users, user);
            Assert.True(await validation.ValidateAsync("ResetPassword", token, users, user));
            Assert.False(await validation.ValidateAsync("EmailConfirmation", token, users, user));
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
            Assert.False(await validation.ValidateAsync("ResetPassword", token, users, user));
            Assert.Equal(passwordBefore, user.PasswordHash);
        });
    }

    private static ServiceProvider KeyProvider(MemoryKeyRepository repository, string application)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName(application).AddKeyManagementOptions(options => options.XmlRepository = repository);
        return services.BuildServiceProvider();
    }

    private static TicketDataFormat CookieFormat(IServiceProvider provider) => new(provider.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", IdentityConstants.ApplicationScheme, "v2"));

    private static DataProtectorTokenProvider<IdentityUser> IdentityTokenProvider(IServiceProvider provider) => new(
        provider.GetRequiredService<IDataProtectionProvider>(), Options.Create(new DataProtectionTokenProviderOptions()),
        NullLogger<DataProtectorTokenProvider<IdentityUser>>.Instance);

    private sealed class MemoryKeyRepository : IXmlRepository
    {
        private readonly List<XElement> elements = new();
        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (elements) return elements.Select(element => new XElement(element)).ToArray();
        }
        public void StoreElement(XElement element, string friendlyName)
        {
            lock (elements) elements.Add(new XElement(element));
        }
    }
}
