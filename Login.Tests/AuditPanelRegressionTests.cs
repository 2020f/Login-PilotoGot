using System.Net;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Controllers;
using Login.Data;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Login.ViewModels.SuperAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class AuditPanelRegressionTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private static string Key() => Guid.NewGuid().ToString("N");
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private async Task<OrdenCreadaResult> Create(bool foreign = false)
    {
        OrdenCreadaResult created = null!;
        await app.InScopeAsync(async sp => created = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(
            foreign ? app.TenantB : app.TenantA, foreign ? app.ShopB : app.ShopA,
            foreign ? app.CustomerB : app.CustomerA, operacionId: Key()));
        return created;
    }

    [Fact]
    public async Task Store_selector_renders_authorized_options_and_switches_with_csrf()
    {
        using var client = await app.LoginWebAsync("multi");
        var html = await client.GetStringAsync("/Tienda/SeleccionarTienda");
        Assert.Contains($"value=\"{app.ShopA}\">A1</option>", html);
        Assert.Contains($"value=\"{app.ShopA2}\">A2</option>", html);
        Assert.DoesNotContain($"value=\"{app.ShopB}\">B1</option>", html);
        Assert.DoesNotContain("Acceso no disponible", html);
        var result = await client.PostAsync("/Tienda/SeleccionarTienda", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["tiendaId"] = app.ShopA2.ToString(), ["__RequestVerificationToken"] = Token(html) }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Contains("/Tienda/Ordenes", result.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tienda/Ordenes")).StatusCode);
    }

    [Fact]
    public async Task Commercial_reference_search_finds_own_order_without_crossing_tenant()
    {
        var own = await Create(); var foreign = await Create(true);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.DatosEnviosOperativos.AddRange(
                new() { ClienteAppId = app.TenantA, OrdenEntregaId = own.OrdenId, Referencia = "REF-audit-unique" },
                new() { ClienteAppId = app.TenantB, OrdenEntregaId = foreign.OrdenId, Referencia = "REF-audit-unique" });
            await db.SaveChangesAsync();
        });
        using var manager = await app.LoginWebAsync("manager-a");
        foreach (var url in new[] { "/Operaciones/Fragmento?seccion=Pedidos&buscar=REF-audit-unique", "/Operaciones/Buscar?buscar=REF-audit-unique" })
        {
            var html = await manager.GetStringAsync(url);
            Assert.Contains($"href=\"/Operaciones/Pedido/{own.OrdenId}\"", html);
            Assert.DoesNotContain($"href=\"/Operaciones/Pedido/{foreign.OrdenId}\"", html);
            Assert.DoesNotContain("Destino privado B", html);
        }
    }

    [Fact]
    public async Task Monitoring_map_response_tracks_newly_started_and_cancelled_routes()
    {
        var order = await Create();
        using var manager = await app.LoginWebAsync("manager-a");
        Assert.Empty((await manager.GetFromJsonAsync<JsonElement>("/Operaciones/MapaDatos")).GetProperty("rutas").EnumerateArray());
        var routeId = 0;
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var actor = await service.ManagerAsync("manager-a");
            var route = Ack(await service.CrearRutaAsync(actor, new("Ruta nueva para mapa", DateOnly.FromDateTime(app.Clock.GetUtcNow().AddHours(-4).DateTime), [order.OrdenId], app.PilotA, null, true, Key())));
            routeId = route.GetProperty("id").GetInt32();
            await service.EstadoRutaAsync(actor, routeId, new("Iniciar", route.GetProperty("version").GetString()!, Key()));
        });
        var active = Assert.Single((await manager.GetFromJsonAsync<JsonElement>("/Operaciones/MapaDatos")).GetProperty("rutas").EnumerateArray());
        Assert.Equal(routeId, active.GetProperty("id").GetInt32()); Assert.Equal("Recogiendo", active.GetProperty("estado").GetString());
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var actor = await service.ManagerAsync("manager-a");
            var route = Assert.Single((await service.SnapshotAsync(actor)).Rutas);
            await service.EstadoRutaAsync(actor, route.Id, new("Cancelar", route.Version, Key()));
        });
        Assert.Empty((await manager.GetFromJsonAsync<JsonElement>("/Operaciones/MapaDatos")).GetProperty("rutas").EnumerateArray());
    }

    [Fact]
    public async Task Incident_history_defaults_to_resolved_cases()
    {
        var order = await Create(); var incidentId = 0;
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var actor = await service.ManagerAsync("manager-a");
            var ack = Ack(await service.ReportarIncidenciaAsync(actor, new("Caso auditado", "Descripción del caso", Key(), order.OrdenId, order.Paradas.Single().ParadaId)));
            incidentId = ack.GetProperty("id").GetInt32();
            var incident = Assert.Single((await service.SnapshotAsync(actor)).Incidencias);
            await service.CambiarIncidenciaAsync(actor, incidentId, new("Resolver", "Caso atendido", incident.Version, Key()));
        });
        using var manager = await app.LoginWebAsync("manager-a");
        var html = await manager.GetStringAsync("/Operaciones/Incidencias?tab=Historial");
        Assert.Contains("data-default-state=\"Resuelta\"", html);
        Assert.Contains($"data-resource-id=\"{incidentId}\" data-resource-kind=\"incidencia\"", html);
        Assert.Contains("data-state=\"Resuelta\"", html);
    }

    [Fact]
    public async Task Selected_stop_is_preserved_and_invalid_incident_context_is_rejected()
    {
        var order = await Create(); var foreign = await Create(true); var stopId = order.Paradas.Single().ParadaId;
        using var manager = await app.LoginWebAsync("manager-a");
        var detail = await manager.GetStringAsync($"/Operaciones/Pedido/{order.OrdenId}?paradaId={stopId}");
        Assert.Contains($"/Operaciones/Incidencias?ordenId={order.OrdenId}&paradaId={stopId}", detail);
        var html = await manager.GetStringAsync($"/Operaciones/Incidencias?ordenId={order.OrdenId}&paradaId={stopId}");
        var coreMatch = Regex.Match(html, "id=\"evModuleCore\">(.*?)</script>", RegexOptions.Singleline);
        using var core = JsonDocument.Parse(coreMatch.Groups[1].Value);
        Assert.Equal(stopId, core.RootElement.GetProperty("paradaId").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/Operaciones/Incidencias?ordenId={order.OrdenId}&paradaId={foreign.Paradas.Single().ParadaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/Operaciones/Incidencias?paradaId={stopId}")).StatusCode);
    }

    [Fact]
    public async Task Superadmin_open_count_excludes_every_terminal_state()
    {
        foreach (var state in Enum.GetValues<EstadoOrden>())
        {
            var order = await Create();
            await app.InScopeAsync(async sp => { var db = sp.GetRequiredService<ApplicationDbContext>(); (await db.OrdenesEntrega.FindAsync(order.OrdenId))!.Estado = state; await db.SaveChangesAsync(); });
        }
        await app.InScopeAsync(async sp =>
        {
            var controller = ActivatorUtilities.CreateInstance<SuperAdminController>(sp);
            controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
            var result = Assert.IsType<ViewResult>(await controller.Dashboard());
            Assert.Equal(4, Assert.IsType<DashboardVm>(result.Model).OrdenesAbiertas);
        });
    }

    [Fact]
    public async Task Merchant_settlement_history_link_tab_and_cash_breakdown_match_selection()
    {
        var settlementId = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>(); var day = DateOnly.FromDateTime(app.Clock.GetUtcNow().DateTime);
            var settlement = new LiquidacionOperativa { ClienteAppId = app.TenantA, TiendaId = app.ShopA, Desde = day, Hasta = day,
                Servicios = -20, EfectivoCompensado = -100, Neto = 80, Conciliada = true,
                Movimientos = [new() { ClienteAppId = app.TenantA, Concepto = "CobrosComercio", Descripcion = "Efectivo recibido por cuenta del comercio", Importe = 100, ClaveOrigen = Key(), ActorId = "manager-a" }] };
            db.LiquidacionesOperativas.Add(settlement); await db.SaveChangesAsync(); settlementId = settlement.Id;
        });
        using var manager = await app.LoginWebAsync("manager-a");
        var history = await manager.GetStringAsync("/Operaciones/Liquidaciones?tab=Historial");
        Assert.Contains($"/Operaciones/Liquidaciones?id={settlementId}&tab=Comercios", history);
        var html = await manager.GetStringAsync($"/Operaciones/Liquidaciones?id={settlementId}");
        Assert.Contains("data-tab=\"Comercios\"", html);
        Assert.Contains("Efectivo recibido por la empresa</dt><dd>+ RD$ 100.00", html);
        var row = Regex.Match(html, $"<tr data-resource-id=\"{settlementId}\" data-resource-kind=\"liquidacion\"[^>]*>(.*?)</tr>", RegexOptions.Singleline);
        Assert.True(row.Success);
        var sortValue = Regex.Match(row.Value, "data-sort-value=\"([^\"]+)\"");
        Assert.True(sortValue.Success);
        // SQL Server conserva escala decimal(18,2); el contrato del atributo es
        // numérico invariante, tanto para "80" como para "80.00".
        Assert.Equal(80m, decimal.Parse(sortValue.Groups[1].Value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
        Assert.Contains("<strong>RD$ 80.00</strong>", row.Value);
    }
}
