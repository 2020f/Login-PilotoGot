using Login.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Login.Tests;

public sealed class GoogleMapsSecurityTests
{
    [Fact]
    public void Missing_browser_key_never_falls_back_to_server_key_and_explicit_browser_key_stays_separate()
    {
        using var http = new HttpClient();
        var serverOnly = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["GoogleMaps:ApiKey"] = "server-only-unit-test-key"
        }).Build();
        var privateService = new GoogleMapsService(http, serverOnly);
        Assert.Equal("server-only-unit-test-key", privateService.ApiKey);
        Assert.Equal("", privateService.BrowserApiKey);

        var separateKeys = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["GoogleMaps:ApiKey"] = "server-only-unit-test-key",
            ["GoogleMaps:BrowserApiKey"] = "browser-only-unit-test-key"
        }).Build();
        var configuredService = new GoogleMapsService(http, separateKeys);
        Assert.Equal("server-only-unit-test-key", configuredService.ApiKey);
        Assert.Equal("browser-only-unit-test-key", configuredService.BrowserApiKey);
    }
}
