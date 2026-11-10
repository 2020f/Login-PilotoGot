using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Login.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Login.Tests;

public sealed class SessionTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task<JsonElement> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "pilot-a@example.test", password = PilotGoFactory.Password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Expired_access_can_be_renewed_without_password_and_refresh_rotates()
    {
        using var client = _app.CreateClient();
        var login = await LoginAsync(client);
        var original = new JwtSecurityTokenHandler().ReadJwtToken(login.GetProperty("token").GetString());
        var expired = new JwtSecurityToken(original.Issuer, original.Audiences.Single(), original.Claims.Where(c => c.Type != "exp" && c.Type != "nbf"),
            DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-2),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PilotGoFactory.JwtKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(expired));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/piloto/mis-ordenes")).StatusCode);
        var oldRefresh = login.GetProperty("refreshToken").GetString();
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldRefresh });
        response.EnsureSuccessStatusCode();
        var renewed = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(oldRefresh, renewed.GetProperty("refreshToken").GetString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", renewed.GetProperty("token").GetString());
        (await client.GetAsync("/api/piloto/mis-ordenes")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldRefresh })).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_this_device_and_keeps_other_device_signed_in()
    {
        using var client = _app.CreateClient();
        var first = await LoginAsync(client);
        var other = await LoginAsync(client);
        var refresh = first.GetProperty("refreshToken").GetString();
        (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = refresh })).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", other.GetProperty("token").GetString());
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Password_change_invalidates_access_and_refresh()
    {
        using var client = _app.CreateClient();
        var login = await LoginAsync(client);
        await _app.InScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await manager.FindByIdAsync("pilot-a"))!;
            Assert.True((await manager.ChangePasswordAsync(user, PilotGoFactory.Password, "New-Test-Password43!")).Succeeded);
        });
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    [Fact]
    public async Task Refresh_expiration_is_enforced_and_database_contains_only_a_hash()
    {
        using var client = _app.CreateClient();
        var login = await LoginAsync(client);
        var refresh = login.GetProperty("refreshToken").GetString()!;
        await _app.InScopeAsync(async sp =>
        {
            var values = await sp.GetRequiredService<ApplicationDbContext>().UserTokens.Select(t => t.Value).ToListAsync();
            Assert.DoesNotContain(values, v => v!.Contains(refresh));
        });
        _app.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_consumes_token_once()
    {
        using var client = _app.CreateClient();
        var login = await LoginAsync(client);
        var body = new { refreshToken = login.GetProperty("refreshToken").GetString() };
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/auth/refresh", body), client.PostAsJsonAsync("/api/auth/refresh", body));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Removed_role_is_revoked_and_refresh_returns_current_roles()
    {
        using var client = _app.CreateClient();
        var login = await LoginAsync(client);
        await _app.InScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await manager.RemoveFromRoleAsync((await manager.FindByIdAsync("pilot-a"))!, "Piloto")).Succeeded);
        });
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/piloto/mis-ordenes")).StatusCode);
        var renewed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.GetProperty("refreshToken").GetString() });
        renewed.EnsureSuccessStatusCode();
        var json = await renewed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(json.GetProperty("roles").EnumerateArray());
        client.DefaultRequestHeaders.Authorization = new("Bearer", json.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/piloto/mis-ordenes")).StatusCode);
    }
}
