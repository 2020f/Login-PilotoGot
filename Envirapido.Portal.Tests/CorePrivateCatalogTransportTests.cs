using System.Net;
using System.Net.Http.Json;
using Envirapido.Portal.Api;
using Envirapido.Portal.Api.Security;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Envirapido.Portal.Tests;

public sealed class CorePrivateCatalogTransportTests
{
    private const string CatalogKey = "Testing-Only-Catalog-Transport-Key-Private";
    private static readonly CrmPortalCatalog Catalog = new([new("plan-transport", "Plan ficticio transporte", 1000, "DOP", 10, 20, 1000)], "transport-fixture-v1");

    [Fact]
    public async Task Production_private_loopback_hop_keeps_HTTPS_routing_without_redirects_or_buyer_credentials()
    {
        var observed = new Observation();
        using var host = Server(IPAddress.Loopback, observed); using var client = host.GetTestClient();
        const string url = "http://127.0.0.1:5000/api/internal/portal/catalog";
        using var baseline = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, baseline.StatusCode);
        Assert.Equal("https://127.0.0.1/api/internal/portal/catalog", baseline.Headers.Location!.AbsoluteUri);
        var source = new PortalCatalogSource(client, new PortalOptions { CatalogUrl = url, CatalogKey = CatalogKey });
        var result = await source.ReadAsync();
        Assert.Equal(Catalog.Version, result.Version); Assert.Equal("plan-transport", Assert.Single(result.Plans).Code);
        Assert.Equal("https", observed.ForwardedProto); Assert.Equal("https", observed.EffectiveScheme);
        Assert.Equal("127.0.0.1:5000", observed.Host);
        Assert.False(observed.CookieSent); Assert.False(observed.AuthorizationSent); Assert.True(observed.CatalogEndpointReached);
    }

    [Fact]
    public async Task A_receiver_that_does_not_trust_the_immediate_peer_still_redirects_and_the_source_fails_closed()
    {
        var observed = new Observation();
        using var host = Server(IPAddress.Parse("203.0.113.99"), observed); using var client = host.GetTestClient();
        var source = new PortalCatalogSource(client, new PortalOptions { CatalogUrl = "http://127.0.0.1:5000/api/internal/portal/catalog", CatalogKey = CatalogKey });
        var failure = await Assert.ThrowsAsync<PortalException>(() => source.ReadAsync());
        Assert.Equal(503, failure.Status); Assert.Equal("https", observed.ForwardedProto);
        Assert.Equal("http", observed.EffectiveScheme); Assert.False(observed.CatalogEndpointReached);
    }

    [Theory]
    [InlineData("http://crm.example.test/api/internal/portal/catalog")]
    [InlineData("http://203.0.113.20/api/internal/portal/catalog")]
    [InlineData("http://user:password@127.0.0.1:5000/api/internal/portal/catalog")]
    [InlineData("http://127.0.0.1:5000/api/internal/portal/catalog#fragment")]
    public async Task Invalid_or_remote_HTTP_is_rejected_before_any_key_or_request_can_leave_the_service(string url)
    {
        using var handler = new CaptureHandler(); using var client = new HttpClient(handler);
        var source = new PortalCatalogSource(client, new PortalOptions { CatalogUrl = url, CatalogKey = CatalogKey });
        var failure = await Assert.ThrowsAsync<PortalException>(() => source.ReadAsync());
        Assert.Equal(503, failure.Status); Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Ordinary_HTTPS_does_not_receive_a_synthetic_forwarded_protocol_header()
    {
        using var handler = new CaptureHandler(); using var client = new HttpClient(handler);
        var source = new PortalCatalogSource(client, new PortalOptions { CatalogUrl = "https://crm.example.test/api/internal/portal/catalog", CatalogKey = CatalogKey });
        await source.ReadAsync(); Assert.Equal(1, handler.Calls); Assert.False(handler.ForwardedProto);
        Assert.False(handler.CookieSent); Assert.False(handler.AuthorizationSent);
    }

    private static IHost Server(IPAddress peer, Observation observed)
        => new HostBuilder().ConfigureWebHost(web => web.UseEnvironment("Production").UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddHostFiltering(o => o.AllowedHosts = ["127.0.0.1", "localhost", "[::1]"]);
                services.AddHttpsRedirection(o => { o.HttpsPort = 443; o.RedirectStatusCode = 307; });
                services.Configure<ForwardedHeadersOptions>(o =>
                {
                    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto; o.ForwardLimit = 1;
                    o.KnownIPNetworks.Clear(); o.KnownProxies.Clear(); o.KnownProxies.Add(IPAddress.Loopback);
                });
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Connection.RemoteIpAddress = peer;
                    observed.ForwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString();
                    observed.CookieSent = context.Request.Headers.ContainsKey("Cookie"); observed.AuthorizationSent = context.Request.Headers.ContainsKey("Authorization");
                    observed.Host = context.Request.Host.Value;
                    await next(context);
                });
                app.UseHostFiltering(); app.UseForwardedHeaders();
                app.Use(async (context, next) => { observed.EffectiveScheme = context.Request.Scheme; await next(context); });
                app.UseHttpsRedirection();
                app.Run(async context =>
                {
                    observed.CatalogEndpointReached = true;
                    if (context.Request.Headers["X-Envirapido-Catalog-Key"] != CatalogKey) { context.Response.StatusCode = 401; return; }
                    await context.Response.WriteAsJsonAsync(Catalog);
                });
            })).Start();
    private sealed class Observation
    {
        public string? ForwardedProto, EffectiveScheme, Host;
        public bool CookieSent, AuthorizationSent, CatalogEndpointReached;
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int Calls;
        public bool ForwardedProto, CookieSent, AuthorizationSent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; ForwardedProto = request.Headers.Contains("X-Forwarded-Proto"); CookieSent = request.Headers.Contains("Cookie"); AuthorizationSent = request.Headers.Contains("Authorization");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Catalog) });
        }
    }
}
