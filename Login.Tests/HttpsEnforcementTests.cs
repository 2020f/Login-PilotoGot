using System.Net;
using System.Text;
using Login.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Login.Tests;

public sealed class ProductionHttpsFixture : IAsyncLifetime
{
    private readonly PilotGoFactory isolated = new();
    public WebApplicationFactory<Program> App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await isolated.SeedAsync();
        App = isolated.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Envirapido:AutomatizacionesHabilitadas", "false");
            builder.UseSetting("AllowedHosts", "envirapido.com;www.envirapido.com;pilotogoapp.zapflow77.dpdns.org");
        });
        Assert.Equal("Production", App.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsSqlServer())
            Assert.StartsWith("PilotGoTest_", db.Database.GetDbConnection().Database);
    }

    public Task DisposeAsync()
    {
        App?.Dispose();
        isolated.Dispose();
        return Task.CompletedTask;
    }
}

public sealed class HttpsEnforcementTests(ProductionHttpsFixture fixture) : IClassFixture<ProductionHttpsFixture>
{
    [Theory]
    [InlineData("envirapido.com", "/", "")]
    [InlineData("www.envirapido.com", "/Identity/Account/Login", "?ReturnUrl=%2FPedidos%3Festado%3DenRuta")]
    [InlineData("pilotogoapp.zapflow77.dpdns.org", "/Identity/Account/Login", "?ReturnUrl=%2FOperaciones")]
    [InlineData("envirapido.com:8080", "/css/site.css", "?v=qa")]
    public async Task HTTP_redirects_before_Identity_and_static_files_preserving_host_path_and_query(
        string host, string path, string query)
    {
        var context = await Send("http", host, path, query);
        Assert.Equal(StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
        var expectedHost = new HostString(host).Host;
        Assert.Equal($"https://{expectedHost}{path}{query}", context.Response.Headers.Location.ToString());
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task HTTP_POST_redirects_without_consuming_credentials_or_changing_the_method()
    {
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("Input.Email=unused%40example.test&Input.Password=fake"));
        var context = await fixture.App.Server.SendAsync(context =>
        {
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("envirapido.com");
            context.Request.Path = "/Identity/Account/Login";
            context.Request.QueryString = new QueryString("?ReturnUrl=%2FPedidos");
            context.Request.Method = "POST";
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.ContentLength = body.Length;
            context.Request.Body = body;
        });
        Assert.Equal(StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
        Assert.Equal("https://envirapido.com/Identity/Account/Login?ReturnUrl=%2FPedidos", context.Response.Headers.Location.ToString());
        Assert.Equal("POST", context.Request.Method);
        Assert.Equal(0, body.Position);
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Trusted_proxy_HTTPS_reaches_login_without_a_loop_and_keeps_cookies_Secure(string proxy)
    {
        var context = await Send("http", "envirapido.com", "/Identity/Account/Login", proxy: proxy, forwardedProto: "https");
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.True(context.Request.IsHttps);
        Assert.Equal(0, context.Response.Headers.Location.Count);
        var csrf = Assert.Single(SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.OfType<string>().ToList()),
            cookie => cookie.Name.ToString().StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.True(csrf.Secure);
        Assert.True(csrf.HttpOnly);
    }

    [Theory]
    [InlineData("203.0.113.19")]
    [InlineData("127.0.0.2")]
    public async Task Untrusted_forwarded_HTTPS_and_Host_cannot_bypass_or_retarget_the_redirect(string proxy)
    {
        var context = await Send("http", "envirapido.com", "/Identity/Account/Login", "?ReturnUrl=%2FPedidos",
            proxy, "https", "attacker.example.test");
        Assert.Equal(StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
        Assert.False(context.Request.IsHttps);
        Assert.Equal("https://envirapido.com/Identity/Account/Login?ReturnUrl=%2FPedidos", context.Response.Headers.Location.ToString());
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Direct_HTTPS_retains_JSON_API_denials_without_redirecting()
    {
        var context = await Send("https", "envirapido.com", "/api/piloto/mis-ordenes");
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Equal(0, context.Response.Headers.Location.Count);
    }

    [Fact]
    public async Task Unapproved_Host_is_rejected_before_a_redirect_can_be_issued()
    {
        var context = await Send("http", "attacker.example.test", "/Identity/Account/Login");
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Headers.Location.Count);
    }

    private Task<HttpContext> Send(string scheme, string host, string path, string query = "",
        string? proxy = null, string? forwardedProto = null, string? forwardedHost = null) =>
        fixture.App.Server.SendAsync(context =>
        {
            context.Request.Scheme = scheme;
            context.Request.Host = new HostString(host);
            context.Request.Path = path;
            context.Request.QueryString = new QueryString(query);
            if (proxy is not null) context.Connection.RemoteIpAddress = IPAddress.Parse(proxy);
            if (forwardedProto is not null) context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
            if (forwardedHost is not null) context.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        });
}
