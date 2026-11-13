using System.Net;
using System.Text.RegularExpressions;
using Login.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Login.Tests;

public sealed class TransportSecurityFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    private static string Csrf(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [Fact]
    public async Task Product_root_redirects_to_HTTPS_after_a_trusted_proxy()
    {
        var response = await app.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("public.example.test");
            context.Request.Path = "/";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        Assert.Equal(StatusCodes.Status302Found, response.Response.StatusCode);
        Assert.Equal("https://public.example.test/Identity/Account/Login?ReturnUrl=%2F", response.Response.Headers.Location);
    }

    [Fact]
    public async Task Product_login_behind_HTTP_proxy_accepts_forwarded_HTTPS_and_emits_secure_CSRF()
    {
        var response = await app.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("public.example.test");
            context.Request.Path = "/Identity/Account/Login";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        var csrf = Assert.Single(SetCookieHeaderValue.ParseList(response.Response.Headers.SetCookie.OfType<string>().ToList()),
            cookie => cookie.Name.ToString().StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.True(csrf.Secure);
        Assert.True(csrf.HttpOnly);
    }

    [Fact]
    public async Task Real_manager_login_emits_secure_session_and_CSRF_cookies_and_keeps_CSRF_enforced()
    {
        using var manager = app.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var login = await manager.GetAsync("/Identity/Account/Login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var csrfCookie = Assert.Single(SetCookieHeaderValue.ParseList(login.Headers.GetValues("Set-Cookie").ToList()),
            c => c.Name.ToString().StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.True(csrfCookie.Secure);
        Assert.True(csrfCookie.HttpOnly);
        var body = await login.Content.ReadAsStringAsync();
        var credentials = new Dictionary<string, string>
        {
            ["Input.Email"] = "manager-a@example.test", ["Input.Password"] = PilotGoFactory.Password
        };
        using var forgedLogin = await manager.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(credentials));
        Assert.Equal(HttpStatusCode.BadRequest, forgedLogin.StatusCode);
        credentials["__RequestVerificationToken"] = Csrf(body);
        using var signed = await manager.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(credentials));
        Assert.Equal(HttpStatusCode.Redirect, signed.StatusCode);
        var sessionCookie = Assert.Single(SetCookieHeaderValue.ParseList(signed.Headers.GetValues("Set-Cookie").ToList()),
            c => c.Name == ".AspNetCore.Identity.Application");
        Assert.True(sessionCookie.Secure);
        Assert.True(sessionCookie.HttpOnly);
        Assert.DoesNotContain("http://", signed.Headers.Location!.OriginalString);
        using var page = await manager.GetAsync("/Operaciones");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var forbiddenMutation = await manager.PostAsync("/Supervisor/ToggleAutoAsignacion",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = app.ShopA.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, forbiddenMutation.StatusCode);
        using var authorizedMutation = await manager.PostAsync("/Supervisor/ToggleAutoAsignacion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = app.ShopA.ToString(), ["__RequestVerificationToken"] = Csrf(await page.Content.ReadAsStringAsync())
        }));
        Assert.Equal(HttpStatusCode.Redirect, authorizedMutation.StatusCode);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            Assert.False((await db.Tiendas.SingleAsync(t => t.Id == app.ShopA)).AutoAsignacion);
            Assert.True((await db.Tiendas.SingleAsync(t => t.Id == app.ShopB)).AutoAsignacion);
        });
    }

    [Fact]
    public async Task Cookie_and_JWT_API_denials_preserve_JSON_contracts_under_HTTPS()
    {
        using var anonymous = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var cookieDenied = await anonymous.GetAsync("/api/operaciones/snapshot");
        Assert.Equal(HttpStatusCode.Unauthorized, cookieDenied.StatusCode);
        Assert.Equal("application/json", cookieDenied.Content.Headers.ContentType?.MediaType);
        Assert.Null(cookieDenied.Headers.Location);
        using var shopWeb = await app.LoginWebAsync("shop-a");
        using var cookieWrongRole = await shopWeb.GetAsync("/api/operaciones/snapshot");
        Assert.Equal(HttpStatusCode.Forbidden, cookieWrongRole.StatusCode);
        Assert.Equal("application/json", cookieWrongRole.Content.Headers.ContentType?.MediaType);
        Assert.Null(cookieWrongRole.Headers.Location);
        using var jwtDenied = await anonymous.GetAsync("/api/piloto/mis-ordenes");
        Assert.Equal(HttpStatusCode.Unauthorized, jwtDenied.StatusCode);
        Assert.Equal("application/json", jwtDenied.Content.Headers.ContentType?.MediaType);
        Assert.Null(jwtDenied.Headers.Location);
        using var shopJwt = await app.LoginAsync("shop-a");
        using var jwtWrongRole = await shopJwt.GetAsync("/api/piloto/mis-ordenes");
        Assert.Equal(HttpStatusCode.Forbidden, jwtWrongRole.StatusCode);
        Assert.Equal("application/json", jwtWrongRole.Content.Headers.ContentType?.MediaType);
        Assert.Null(jwtWrongRole.Headers.Location);
    }
}
