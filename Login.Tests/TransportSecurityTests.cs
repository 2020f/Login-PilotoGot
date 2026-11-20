using System.Net;
using Login.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace Login.Tests;

public sealed class TransportSecurityTests
{
    private static IHost CreateServer(string environment = "Production", string? proxy = null, bool automaticForwarding = false)
        => new HostBuilder().ConfigureWebHost(web => web.UseEnvironment(environment).UseTestServer()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(proxy is null
                ? [] : new Dictionary<string, string?> { ["ReverseProxy:KnownProxies:0"] = proxy }))
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(logging => logging.ClearProviders());
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(IdentityConstants.ApplicationScheme)
                    .AddCookie(IdentityConstants.ApplicationScheme,
                        options => options.LoginPath = "/Identity/Account/Login");
                if (automaticForwarding)
                    services.Configure<ForwardedHeadersOptions>(options =>
                    {
                        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                        options.KnownIPNetworks.Clear();
                        options.KnownProxies.Clear();
                    });
                services.AddTransportSecurity(context.Configuration, context.HostingEnvironment);
            })
            .Configure(app =>
            {
                app.UseForwardedHeaders();
                if (automaticForwarding) app.UseForwardedHeaders();
                app.UseAuthentication();
                app.Run(context => context.ChallengeAsync(IdentityConstants.ApplicationScheme));
            })).Start();

    private static Task<HttpContext> Challenge(IHost server, string remote, string proto = "https", string? forwardedHost = null)
        => server.GetTestServer().SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("public.example.test");
            context.Request.Path = "/";
            context.Request.Headers["X-Forwarded-Proto"] = proto;
            context.Request.Headers["X-Forwarded-For"] = "198.51.100.42";
            if (forwardedHost is not null) context.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        });

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task Exact_loopback_proxy_preserves_HTTPS_in_login_redirects(string remote)
    {
        using var server = CreateServer();
        var response = await Challenge(server, remote);
        Assert.Equal(StatusCodes.Status302Found, response.Response.StatusCode);
        Assert.Equal("https://public.example.test/Identity/Account/Login?ReturnUrl=%2F", response.Response.Headers.Location);
        // X-Forwarded-For is intentionally outside this change's trust boundary.
        Assert.Equal(IPAddress.Parse(remote), response.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData("203.0.113.19")]
    [InlineData("127.0.0.2")]
    public async Task Unknown_proxy_cannot_spoof_HTTPS(string remote)
    {
        using var server = CreateServer();
        var response = await Challenge(server, remote);
        Assert.StartsWith("http://public.example.test/", response.Response.Headers.Location.ToString());
        Assert.False(response.Request.IsHttps);
    }

    [Theory]
    [InlineData("10.20.30.40")]
    [InlineData("::ffff:10.20.30.40")]
    public async Task Explicit_proxy_IP_is_accepted_in_native_and_mapped_form(string remote)
    {
        using var server = CreateServer(proxy: "10.20.30.40");
        var response = await Challenge(server, remote);
        Assert.True(response.Request.IsHttps);
        Assert.StartsWith("https://public.example.test/", response.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Forwarded_host_cannot_change_redirect_destination()
    {
        using var server = CreateServer();
        var response = await Challenge(server, "127.0.0.1", forwardedHost: "attacker.example");
        Assert.Equal("public.example.test", response.Request.Host.Value);
        Assert.StartsWith("https://public.example.test/", response.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("http, https", true)]
    [InlineData("https, http", false)]
    public async Task Only_the_immediate_proxy_protocol_is_consumed(string header, bool isHttps)
    {
        using var server = CreateServer();
        var response = await Challenge(server, "127.0.0.1", header);
        Assert.Equal(isHttps, response.Request.IsHttps);
    }

    [Fact]
    public async Task Hosting_defaults_and_duplicate_registration_cannot_broaden_proxy_trust()
    {
        using var server = CreateServer(automaticForwarding: true);
        var unknown = await Challenge(server, "203.0.113.19");
        Assert.False(unknown.Request.IsHttps);
        var trusted = await Challenge(server, "127.0.0.1", "http, https");
        Assert.True(trusted.Request.IsHttps);
        Assert.Equal("http", trusted.Request.Headers["X-Forwarded-Proto"].ToString());
        Assert.Equal(IPAddress.Loopback, trusted.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.20.30.0/24")]
    [InlineData("proxy.example.test")]
    public void Invalid_or_network_proxy_configuration_fails_closed(string proxy)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateServer(proxy: proxy));
        Assert.Contains("ReverseProxy", exception.Message);
    }

    [Theory]
    [InlineData("Production", CookieSecurePolicy.Always)]
    [InlineData("Staging", CookieSecurePolicy.Always)]
    [InlineData("Testing", CookieSecurePolicy.Always)]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    public void Session_antiforgery_and_tempdata_cookies_require_HTTPS_outside_development(string environment, CookieSecurePolicy expected)
    {
        using var server = CreateServer(environment);
        var cookies = server.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        foreach (var scheme in new[] { IdentityConstants.ApplicationScheme, IdentityConstants.ExternalScheme,
            IdentityConstants.TwoFactorRememberMeScheme, IdentityConstants.TwoFactorUserIdScheme })
        {
            Assert.Equal(expected, cookies.Get(scheme).Cookie.SecurePolicy);
            Assert.True(cookies.Get(scheme).Cookie.HttpOnly);
        }
        var csrf = server.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal(expected, csrf.Cookie.SecurePolicy);
        Assert.True(csrf.Cookie.HttpOnly);
        var temp = server.Services.GetRequiredService<IOptions<CookieTempDataProviderOptions>>().Value;
        Assert.Equal(expected, temp.Cookie.SecurePolicy);
        Assert.True(temp.Cookie.HttpOnly);
        var forwarding = server.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.XForwardedProto, forwarding.ForwardedHeaders);
        Assert.Equal(1, forwarding.ForwardLimit);
        Assert.Empty(forwarding.KnownIPNetworks);
    }
}
