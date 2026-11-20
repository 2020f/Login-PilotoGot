using System.Net;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class PanelOperationsTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "La página debe incluir un token antifalsificación.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<HttpClient> ManagerAsync()
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var page = await client.GetStringAsync("/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "manager-a@example.test", ["Input.Password"] = PilotGoFactory.Password,
            ["__RequestVerificationToken"] = Token(page)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> input)
    {
        input["__RequestVerificationToken"] = Token(await client.GetStringAsync("/Supervisor/Ordenes"));
        return await client.PostAsync(path, new FormUrlEncodedContent(input));
    }

    [Fact]
    public async Task Every_pilot_deactivation_route_rejects_open_deliveries()
    {
        await _app.InScopeAsync(async sp =>
        {
            var orders = sp.GetRequiredService<IOrdenService>();
            var order = await orders.CrearOrdenAsync(_app.TenantA, _app.ShopA, _app.CustomerA);
            await orders.AsignarPilotoAsync(order.CodigoB, _app.PilotA, "manager-a", "Gestor", order.OrdenId);
        });
        using var manager = await ManagerAsync();
        foreach (var route in new[] { "DesactivarPiloto", "EliminarPiloto", "EditarPiloto" })
        {
            var response = await PostAsync(manager, $"/Supervisor/{route}", new()
            {
                ["Id"] = _app.PilotA.ToString(), ["Nombre"] = "Piloto A", ["Telefono"] = "8090001", ["Activo"] = "false",
                ["EmailPiloto"] = "pilot-a@example.test"
            });
            Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);
            await _app.InScopeAsync(async sp => Assert.True((await sp.GetRequiredService<ApplicationDbContext>().Pilotos.FindAsync(_app.PilotA))!.Activo));
        }
    }

    [Fact]
    public async Task Store_edit_and_deactivate_share_pending_order_guard()
    {
        await _app.InScopeAsync(async sp => await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(_app.TenantA, _app.ShopA, _app.CustomerA));
        using var manager = await ManagerAsync();
        foreach (var route in new[] { "DesactivarTienda", "EditarTienda" })
        {
            var response = await PostAsync(manager, $"/Supervisor/{route}", new()
            {
                ["Id"] = _app.ShopA.ToString(), ["Nombre"] = "Comercio A", ["Direccion"] = "Calle A", ["Telefono"] = "8090001", ["Activo"] = "false"
            });
            Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);
            await _app.InScopeAsync(async sp => Assert.True((await sp.GetRequiredService<ApplicationDbContext>().Tiendas.FindAsync(_app.ShopA))!.Activo));
        }
    }

    [Fact]
    public async Task Deactivating_one_store_does_not_lock_its_principal_identity()
    {
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.UsuariosTienda.SingleAsync(u => u.IdentityUserId == "shop-a"))!.EsPrincipal = true;
            await db.SaveChangesAsync();
        });
        using var manager = await ManagerAsync();
        var response = await PostAsync(manager, "/Supervisor/DesactivarTienda", new() { ["Id"] = _app.ShopA.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.False((await db.Tiendas.FindAsync(_app.ShopA))!.Activo);
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.False(await users.IsLockedOutAsync((await users.FindByIdAsync("shop-a"))!));
        });
    }

    [Fact]
    public async Task Reactivations_cannot_bypass_effective_pilot_override()
    {
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.Pilotos.FindAsync(_app.PilotA))!.Activo = false;
            (await db.ClientesApp.FindAsync(_app.TenantA))!.MaxPilotos = 1;
            await db.SaveChangesAsync();
        });
        using var manager = await ManagerAsync();
        var response = await PostAsync(manager, "/Supervisor/EditarPiloto", new()
        {
            ["Id"] = _app.PilotA.ToString(), ["Nombre"] = "Piloto A", ["Telefono"] = "8090001", ["Activo"] = "true",
            ["EmailPiloto"] = "pilot-a@example.test"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Límite", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await _app.InScopeAsync(async sp => Assert.False((await sp.GetRequiredService<ApplicationDbContext>().Pilotos.FindAsync(_app.PilotA))!.Activo));
    }

    [Fact]
    public async Task Zero_means_unlimited_and_distinct_users_consume_only_one_slot()
    {
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var tenant = await db.ClientesApp.Include(c => c.Plan).SingleAsync(c => c.Id == _app.TenantA);
            tenant.Plan.MaxTiendas = 0; tenant.MaxPilotos = 0; tenant.MaxOrdenesMes = 0; tenant.MaxUsuarios = 0;
            await db.SaveChangesAsync();
            var quota = sp.GetRequiredService<QuotaPolicyService>();
            var effective = await quota.ResolveAsync(_app.TenantA);
            Assert.Equal(0, effective.MaxTiendas); Assert.Equal(0, effective.MaxPilotos); Assert.Equal(0, effective.MaxOrdenesMes);
            await quota.EnsureNewStoreAsync(_app.TenantA, true); await quota.EnsureNewPilotAsync(_app.TenantA);
            // Dos tiendas de la misma identidad 'multi' no consumen dos usuarios.
            Assert.Equal(5, await quota.CountUsersAsync(_app.TenantA));
        });
    }

    [Fact]
    public async Task Dispatch_fragment_and_history_only_show_the_managers_tenant()
    {
        int own = 0, other = 0;
        await _app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrdenService>();
            own = (await service.CrearOrdenAsync(_app.TenantA, _app.ShopA, _app.CustomerA)).OrdenId;
            other = (await service.CrearOrdenAsync(_app.TenantB, _app.ShopB, _app.CustomerB)).OrdenId;
        });
        using var manager = await ManagerAsync();
        var fragment = await manager.GetStringAsync("/Supervisor/OrdenesFragmento");
        // Los tokens CSRF son aleatorios y pueden contener "B1". Comprobar
        // el dato de comercio renderizado, no cualquier subcadena del HTML.
        var storeNames = Regex.Matches(fragment, "<div class=\"pg-store-name\">([^<]*)</div>")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value).Trim()).ToArray();
        Assert.Equal(new[] { "A1" }, storeNames);
        Assert.DoesNotContain(">Todos</div>", fragment);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/Supervisor/DetalleOrden/{other}")).StatusCode);
        var detail = await manager.GetStringAsync($"/Supervisor/DetalleOrden/{own}");
        Assert.Contains("Historial de la orden", detail);
        Assert.Contains("Comercio", detail);
    }

    [Fact]
    public async Task Invalid_statistics_range_returns_a_readable_error_instead_of_throwing()
    {
        using var manager = await ManagerAsync();
        var response = await manager.GetAsync("/Supervisor/Estadisticas?desde=2026-11-20&hasta=2026-11-01");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=", response.Headers.Location!.ToString());
    }
}
