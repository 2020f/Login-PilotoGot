using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Login.Areas.Identity.Pages.Account;
using Login.Data;
using Login.Domain.Entities;
using Login.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Login.Tests;

public sealed class AuditIdentityRegressionTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    private const string ReplacementPassword = "QA-recovery-replacement42!";
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static string Csrf(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "La página debe emitir CSRF.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task PrincipalAsync()
        => await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.UsuariosTienda.SingleAsync(m => m.TiendaId == _app.ShopA && m.IdentityUserId == "shop-a")).EsPrincipal = true;
            await db.SaveChangesAsync();
        });

    private static async Task<HttpClient> LoginWebAsync(WebApplicationFactory<Program> host, string id)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var page = await client.GetStringAsync("/Identity/Account/Login");
        var login = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = id + "@example.test", ["Input.Password"] = PilotGoFactory.Password,
            ["__RequestVerificationToken"] = Csrf(page)
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private async Task<HttpResponseMessage> ResetByManagerAsync(HttpClient manager, int? shopId = null)
    {
        var page = await manager.GetStringAsync($"/Supervisor/EditarTienda/{_app.ShopA}");
        return await manager.PostAsync("/Supervisor/ResetPasswordTienda", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TiendaId"] = (shopId ?? _app.ShopA).ToString(), ["NuevoPassword"] = ReplacementPassword,
            ["__RequestVerificationToken"] = Csrf(page)
        }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manager_cannot_replace_global_credential_with_any_external_store_membership(bool active)
    {
        await PrincipalAsync();
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.UsuariosTienda.Add(new() { IdentityUserId = "shop-a", TiendaId = _app.ShopB, Activo = active });
            await db.SaveChangesAsync();
        });
        string? stamp = null;
        await _app.InScopeAsync(async sp => stamp = (await sp.GetRequiredService<UserManager<IdentityUser>>().FindByIdAsync("shop-a"))!.SecurityStamp);
        using var manager = await _app.LoginWebAsync("manager-a");
        var reset = await ResetByManagerAsync(manager);
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Contains("error=", reset.Headers.Location!.OriginalString);
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByIdAsync("shop-a"))!;
            Assert.Equal(stamp, user.SecurityStamp);
            Assert.True(await users.CheckPasswordAsync(user, PilotGoFactory.Password));
            Assert.False(await users.CheckPasswordAsync(user, ReplacementPassword));
        });
        using var unauthorized = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthorized.PostAsJsonAsync("/api/auth/login",
            new { email = "shop-a@example.test", password = ReplacementPassword })).StatusCode);
    }

    [Theory]
    [InlineData("Gestor")]
    [InlineData("Piloto")]
    [InlineData("SuperAdmin")]
    public async Task Manager_cannot_reset_a_principal_with_another_global_role(string role)
    {
        await PrincipalAsync();
        await _app.InScopeAsync(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new(role))).Succeeded);
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync("shop-a"))!, role)).Succeeded);
        });
        using var manager = await _app.LoginWebAsync("manager-a");
        var reset = await ResetByManagerAsync(manager);
        Assert.Contains("error=", reset.Headers.Location!.OriginalString);
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync("shop-a"))!, PilotGoFactory.Password));
        });
    }

    [Fact]
    public async Task Exclusive_commercial_identity_can_be_reset_without_losing_own_tenant_memberships()
    {
        await PrincipalAsync();
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.UsuariosTienda.Add(new() { IdentityUserId = "shop-a", TiendaId = _app.ShopA2, Activo = true });
            await db.SaveChangesAsync();
        });
        using var previousSession = await _app.LoginAsync("shop-a");
        using var manager = await _app.LoginWebAsync("manager-a");
        var reset = await ResetByManagerAsync(manager);
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Contains("ok=", reset.Headers.Location!.OriginalString);
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByIdAsync("shop-a"))!;
            Assert.True(await users.CheckPasswordAsync(user, ReplacementPassword));
            Assert.False(await users.CheckPasswordAsync(user, PilotGoFactory.Password));
            Assert.Equal(2, await sp.GetRequiredService<ApplicationDbContext>().UsuariosTienda.CountAsync(m => m.IdentityUserId == "shop-a" && m.Activo));
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await previousSession.GetAsync("/api/tienda/ordenes")).StatusCode);
    }

    [Fact]
    public async Task Foreign_shop_cannot_be_selected_for_manager_reset()
    {
        await PrincipalAsync();
        using var manager = await _app.LoginWebAsync("manager-a");
        var reset = await ResetByManagerAsync(manager, _app.ShopB);
        Assert.Contains("error=", reset.Headers.Location!.OriginalString);
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync("shop-b"))!, PilotGoFactory.Password));
        });
    }

    private WebApplicationFactory<Program> RecoveryHost(CapturingEmail sender, bool expired = false)
        => _app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccountEmailSender>(); services.RemoveAll<IEmailSender>();
            services.AddSingleton<IAccountEmailSender>(sender); services.AddSingleton<IEmailSender>(sender);
            if (expired) services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromSeconds(-1));
        }));

    private static async Task<HttpResponseMessage> RequestRecoveryAsync(HttpClient client, string email)
    {
        var page = await client.GetStringAsync("/Identity/Account/ForgotPassword");
        return await client.PostAsync("/Identity/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Input.Email"] = email, ["__RequestVerificationToken"] = Csrf(page) }));
    }

    private static Uri Link(CapturingEmail sender)
    {
        var message = Assert.Single(sender.Messages);
        var href = Regex.Match(message.Body, "href=\"([^\"]+)\"");
        Assert.True(href.Success);
        return new(WebUtility.HtmlDecode(href.Groups[1].Value));
    }

    private static async Task<HttpResponseMessage> SubmitRecoveryAsync(HttpClient client, Uri link, string password)
    {
        var query = QueryHelpers.ParseQuery(link.Query);
        var page = await client.GetStringAsync(link.PathAndQuery);
        return await client.PostAsync("/Identity/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = query["email"].ToString(), ["Input.Code"] = query["code"].ToString(),
            ["Input.Password"] = password, ["Input.ConfirmPassword"] = password,
            ["__RequestVerificationToken"] = Csrf(page)
        }));
    }

    [Fact]
    public async Task Email_recovery_is_non_enumerating_and_uses_canonical_single_use_link()
    {
        var mail = new CapturingEmail();
        using var host = RecoveryHost(mail);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var known = await RequestRecoveryAsync(client, "shop-a@example.test");
        var unknown = await RequestRecoveryAsync(client, "not-registered@example.test");
        Assert.Equal(HttpStatusCode.Redirect, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(known.Headers.Location, unknown.Headers.Location);
        Assert.Single(mail.Messages);
        var link = Link(mail);
        Assert.Equal("accounts.example.test", link.Host);
        Assert.Equal("https", link.Scheme);
        var changed = await SubmitRecoveryAsync(client, link, ReplacementPassword);
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Contains("ResetPasswordConfirmation", changed.Headers.Location!.OriginalString);
        var reused = await SubmitRecoveryAsync(client, link, "QA-attempt-replay42!");
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.Contains(ResetPasswordModel.InvalidLinkMessage, WebUtility.HtmlDecode(await reused.Content.ReadAsStringAsync()));
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>(); var user = (await users.FindByIdAsync("shop-a"))!;
            Assert.True(await users.CheckPasswordAsync(user, ReplacementPassword));
            Assert.False(await users.CheckPasswordAsync(user, "QA-attempt-replay42!"));
            Assert.Single(await users.GetRolesAsync(user));
            Assert.Equal(1, await sp.GetRequiredService<ApplicationDbContext>().UsuariosTienda.CountAsync(m => m.IdentityUserId == "shop-a"));
        });
    }

    [Fact]
    public async Task Expired_password_link_cannot_change_credentials()
    {
        var mail = new CapturingEmail();
        using var host = RecoveryHost(mail, expired: true);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await RequestRecoveryAsync(client, "shop-a@example.test")).StatusCode);
        var result = await SubmitRecoveryAsync(client, Link(mail), ReplacementPassword);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains(ResetPasswordModel.InvalidLinkMessage, WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync()));
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync("shop-a"))!, PilotGoFactory.Password));
        });
    }

    [Fact]
    public async Task Unconfigured_email_reports_unavailable_for_known_and_unknown_addresses()
    {
        var mail = new CapturingEmail { IsConfigured = false };
        using var host = RecoveryHost(mail);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var email in new[] { "shop-a@example.test", "not-registered@example.test" })
        {
            var response = await RequestRecoveryAsync(client, email);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains(ForgotPasswordModel.UnavailableMessage, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
        Assert.Empty(mail.Messages);
    }

    [Fact]
    public async Task Delivery_failure_keeps_public_result_non_enumerating()
    {
        var mail = new CapturingEmail { FailDelivery = true };
        using var host = RecoveryHost(mail);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var known = await RequestRecoveryAsync(client, "shop-a@example.test");
        var unknown = await RequestRecoveryAsync(client, "not-registered@example.test");
        Assert.Equal(HttpStatusCode.Redirect, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(known.Headers.Location, unknown.Headers.Location);
        Assert.Empty(mail.Messages);
    }

    [Fact]
    public async Task Confirmation_page_validates_identity_token_without_adding_business_access()
    {
        var mail = new CapturingEmail();
        using var host = RecoveryHost(mail);
        using var client = host.CreateClient();
        string encoded;
        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync((await users.FindByIdAsync("shop-a"))!)));
        }
        var confirmation = await client.GetStringAsync("/Identity/Account/ConfirmEmail?userId=shop-a&code=" + Uri.EscapeDataString(encoded));
        Assert.Contains("Correo confirmado", confirmation);
        Assert.Contains("Enlace no", await client.GetStringAsync("/Identity/Account/ConfirmEmail?userId=shop-b&code=" + Uri.EscapeDataString(encoded)));
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.FindByIdAsync("shop-a"))!.EmailConfirmed);
            Assert.False((await users.FindByIdAsync("shop-b"))!.EmailConfirmed);
            Assert.Single(await users.GetRolesAsync((await users.FindByIdAsync("shop-a"))!));
        });
    }

    [Fact]
    public async Task Register_without_email_configuration_preserves_existing_sign_in_flow_without_fake_delivery()
    {
        var mail = new CapturingEmail { IsConfigured = false };
        using var host = RecoveryHost(mail);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var page = await client.GetStringAsync("/Identity/Account/Register");
        var response = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "qa-new-account@example.test", ["Input.Password"] = PilotGoFactory.Password,
            ["Input.ConfirmPassword"] = PilotGoFactory.Password, ["__RequestVerificationToken"] = Csrf(page)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(mail.Messages);
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByEmailAsync("qa-new-account@example.test"))!;
            Assert.NotNull(user);
            Assert.Empty(await users.GetRolesAsync(user));
            Assert.False(await sp.GetRequiredService<ApplicationDbContext>().UsuariosTienda.AnyAsync(m => m.IdentityUserId == user.Id));
        });
    }

    [Fact]
    public async Task Unconfigured_real_sender_refuses_delivery_and_rejects_unsafe_canonical_urls()
    {
        var sender = new EmailSender(Options.Create(new AccountEmailOptions()), NullLogger<EmailSender>.Instance);
        Assert.False(sender.IsConfigured);
        await Assert.ThrowsAsync<AccountEmailDeliveryException>(() => sender.SendEmailAsync("qa@example.test", "Prueba", "No entregar"));
        Assert.Null(AccountEmailOptions.ReadPublicBaseUri("http://public.example.test"));
        Assert.Null(AccountEmailOptions.ReadPublicBaseUri("https://user:secret@example.test"));
        Assert.Null(AccountEmailOptions.ReadPublicBaseUri("https://example.test/?redirect=evil"));
        Assert.NotNull(AccountEmailOptions.ReadPublicBaseUri("https://pilotogoapp.zapflow77.dpdns.org"));
    }

    private sealed class CapturingEmail : IAccountEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool FailDelivery { get; set; }
        public Uri? PublicBaseUri => new("https://accounts.example.test/");
        public ConcurrentQueue<(string Email, string Body)> Messages { get; } = new();
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (!IsConfigured || FailDelivery) throw new AccountEmailDeliveryException();
            Messages.Enqueue((email, htmlMessage));
            return Task.CompletedTask;
        }
    }
}
