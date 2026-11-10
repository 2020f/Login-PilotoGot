using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Login.Tests;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void Production_rejects_the_published_development_signing_key()
    {
        using var production = new ProductionFactory();
        var error = Assert.Throws<InvalidOperationException>(() => production.CreateClient());
        Assert.Contains("Jwt__Key", error.Message);
    }

    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:Key", "PilotGo_Dev_Only_Secret_Key_ChangeMe_1234567890");
        }
    }
}
