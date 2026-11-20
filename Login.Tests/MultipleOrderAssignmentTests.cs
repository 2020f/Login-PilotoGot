using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class MultipleOrderAssignmentTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private static string Key() => Guid.NewGuid().ToString("N");
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.GetUtcNow(),
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")).DateTime);
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private async Task<HttpClient> Pilot(string id = "pilot-a")
    {
        var client = await app.LoginAsync(id);
        client.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "3");
        return client;
    }

    private static async Task<OrdenCreadaResult> Create(HttpClient shop, int customer)
    {
        var response = await shop.PostAsJsonAsync("/api/tienda/ordenes",
            new { usuarioFinalId = customer, notaPedido = "QA varias órdenes", operacionId = Key() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrdenCreadaResult>())!;
    }

    private static async Task Accept(HttpClient pilot, OrdenCreadaResult order)
        => (await pilot.PostAsync($"/api/piloto/ordenes-auto/{order.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();

    private static async Task Pickup(HttpClient pilot, OrdenCreadaResult order)
        => (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta",
            new { order.OrdenId, order.CodigoB, operacionId = Key() })).EnsureSuccessStatusCode();

    private static async Task Deliver(HttpClient pilot, OrdenCreadaResult order)
    {
        var stop = Assert.Single(order.Paradas);
        (await pilot.PostAsJsonAsync($"/api/piloto/ordenes/{order.OrdenId}/paradas/{stop.ParadaId}/confirmar-entrega",
            new { stop.CodigoC, operacionId = Key() })).EnsureSuccessStatusCode();
    }

    private Task Assign(OrdenCreadaResult order)
        => app.InScopeAsync(sp => sp.GetRequiredService<IOrdenService>()
            .AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_pickup_or_delivery_keeps_new_auto_offers_and_manual_assignments_with_original_session(bool collected)
    {
        using var shop = await app.LoginAsync("shop-a");
        using var pilot = await Pilot();
        var first = await Create(shop, app.CustomerA);
        var manual = await Create(shop, app.CustomerA);
        var nextAuto = await Create(shop, app.CustomerA);
        await Accept(pilot, first);
        if (collected) await Pickup(pilot, first);

        var offers = (await pilot.GetFromJsonAsync<List<OrdenAutoDisponibleDto>>("/api/piloto/ordenes-auto"))!;
        Assert.Equal(new[] { manual.OrdenId, nextAuto.OrdenId }, offers.Select(o => o.OrdenId).OrderBy(id => id));
        (await pilot.PostAsJsonAsync("/api/piloto/estado", new { enLinea = false })).EnsureSuccessStatusCode();
        await Assign(manual); // Comercio automático; asignación manual incluso con recepción OFF.
        var assignedOffline = (await pilot.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!;
        Assert.Equal(2, assignedOffline.Count);
        Assert.Contains(assignedOffline, o => o.OrdenId == manual.OrdenId);
        Assert.Empty((await pilot.GetFromJsonAsync<List<OrdenAutoDisponibleDto>>("/api/piloto/ordenes-auto"))!);
        (await pilot.PostAsJsonAsync("/api/piloto/estado", new { enLinea = true })).EnsureSuccessStatusCode();
        await Accept(pilot, nextAuto);
        Assert.Equal(3, (await pilot.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!.Count);
        if (!collected) await Pickup(pilot, first);
        await Deliver(pilot, first);

        var remaining = (await pilot.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!;
        Assert.Equal(new[] { manual.OrdenId, nextAuto.OrdenId }, remaining.Select(o => o.OrdenId).OrderBy(id => id));
        var activeTracking = (await pilot.GetFromJsonAsync<OperationsActivity>("/api/piloto/operaciones/actividad"))!;
        Assert.True(activeTracking.RequiereGps);
        Assert.NotNull(activeTracking.AutorizacionGpsHasta);
        Assert.True(activeTracking.AutorizacionGpsHasta > activeTracking.ActualizadoAt);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Entregada, (await db.OrdenesEntrega.FindAsync(first.OrdenId))!.Estado);
            foreach (var order in new[] { manual, nextAuto })
            {
                var row = (await db.OrdenesEntrega.FindAsync(order.OrdenId))!;
                Assert.Equal(app.PilotA, row.PilotoId);
                Assert.Equal(EstadoOrden.Asignada, row.Estado);
                Assert.True(row.EsAuto);
                Assert.False(await db.CodigosEntrega.AnyAsync(c => c.OrdenEntregaId == row.Id && c.Usado));
            }
            var codes = await db.CodigosEntrega.ToListAsync();
            Assert.Equal(6, codes.Count);
            Assert.Equal(6, codes.Select(c => c.Codigo).Distinct().Count());
            Assert.Equal(2, codes.Count(c => c.Usado));
            Assert.Equal(3, await db.HistorialOrdenes.CountAsync(h => h.TipoEvento == TipoEventoOrden.Asignada));
            Assert.False(await db.RutaOrdenesOperativas.AnyAsync());
        });
        await Pickup(pilot, manual);
        await Deliver(pilot, manual);
        await Pickup(pilot, nextAuto);
        await Deliver(pilot, nextAuto);
        var finishedTracking = (await pilot.GetFromJsonAsync<OperationsActivity>("/api/piloto/operaciones/actividad"))!;
        Assert.False(finishedTracking.RequiereGps);
        Assert.Null(finishedTracking.AutorizacionGpsHasta);
    }

    [Fact]
    public async Task Busy_pilots_competing_for_the_same_offer_still_commit_only_one_new_custodian_and_history()
    {
        using var shop = await app.LoginAsync("shop-a");
        using var firstPilot = await Pilot();
        using var secondPilot = await Pilot("pilot-a2");
        await Accept(firstPilot, await Create(shop, app.CustomerA));
        await Accept(secondPilot, await Create(shop, app.CustomerA));
        var contested = await Create(shop, app.CustomerA);
        var responses = await Task.WhenAll(
            firstPilot.PostAsync($"/api/piloto/ordenes-auto/{contested.OrdenId}/aceptar", null),
            secondPilot.PostAsync($"/api/piloto/ordenes-auto/{contested.OrdenId}/aceptar", null));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var order = (await db.OrdenesEntrega.FindAsync(contested.OrdenId))!;
            Assert.Contains(order.PilotoId!.Value, new[] { app.PilotA, app.PilotA2 });
            Assert.Equal(EstadoOrden.Asignada, order.Estado);
            Assert.Equal(1, await db.HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == order.Id && h.TipoEvento == TipoEventoOrden.Asignada));
            Assert.Equal(3, await db.OrdenesEntrega.CountAsync(o => o.Estado == EstadoOrden.Asignada));
            Assert.False(await db.CodigosEntrega.AnyAsync(c => c.Usado));
        });
    }

    [Fact]
    public async Task Active_route_accepts_separate_auto_and_manual_work_without_rewriting_its_points_or_next_visit()
    {
        using var shop = await app.LoginAsync("shop-a");
        using var pilot = await Pilot();
        var first = await Create(shop, app.CustomerA);
        var second = await Create(shop, app.CustomerA);
        var separateAuto = await Create(shop, app.CustomerA);
        var separateManual = await Create(shop, app.CustomerA);
        var routeId = 0;
        await app.InScopeAsync(async sp =>
        {
            var operations = sp.GetRequiredService<EnvirapidoOperationsService>();
            var manager = await operations.ManagerAsync("manager-a");
            routeId = Ack(await operations.CrearRutaAsync(manager,
                new("Ruta que conserva su secuencia", Today, [first.OrdenId, second.OrdenId], app.PilotA, null, false, Key())))
                .GetProperty("id").GetInt32();
            var planned = Assert.Single((await operations.SnapshotAsync(await operations.PilotAsync("pilot-a"))).Rutas);
            await operations.EstadoRutaAsync(manager, routeId, new("Iniciar", planned.Version, Key()));
        });
        await Accept(pilot, separateAuto);
        await Assign(separateManual);
        var blocked = await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta",
            new { second.OrdenId, second.CodigoB, operacionId = Key() });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var memberships = await db.RutaOrdenesOperativas.Where(x => x.RutaId == routeId && x.Vigente)
                .OrderBy(x => x.OrdenEntregaId).Select(x => x.OrdenEntregaId).ToArrayAsync();
            Assert.Equal(new[] { first.OrdenId, second.OrdenId }, memberships);
            var points = await db.PuntosRutaOperativos.Where(x => x.RutaId == routeId).OrderBy(x => x.Posicion).ToListAsync();
            Assert.Equal(new[] { first.OrdenId, second.OrdenId, first.OrdenId, second.OrdenId }, points.Select(p => p.OrdenEntregaId));
            Assert.Equal(new[] { TipoPuntoRuta.Recogida, TipoPuntoRuta.Recogida, TipoPuntoRuta.Entrega, TipoPuntoRuta.Entrega }, points.Select(p => p.Tipo));
            Assert.Equal(new[] { 1, 2, 3, 4 }, points.Select(p => p.Posicion));
            Assert.False(await db.CodigosEntrega.AnyAsync(c => c.Usado));
        });
        await Pickup(pilot, first);
        await Pickup(pilot, second);
        await Deliver(pilot, first);
        await Deliver(pilot, second);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoRutaOperativa.Finalizada, (await db.RutasOperativas.FindAsync(routeId))!.Estado);
            Assert.False(await db.RutaOrdenesOperativas.AnyAsync(x => x.RutaId == routeId && x.Vigente));
            Assert.Equal(2, await db.OrdenesEntrega.CountAsync(o => o.PilotoId == app.PilotA && o.Estado == EstadoOrden.Asignada));
            Assert.False(await db.CodigosEntrega.AnyAsync(c => (c.OrdenEntregaId == separateAuto.OrdenId || c.OrdenEntregaId == separateManual.OrdenId) && c.Usado));
        });
    }

    [Fact]
    public async Task Occupied_pilot_still_cannot_take_scheduled_reserved_or_paused_orders_by_ID()
    {
        using var shop = await app.LoginAsync("shop-a");
        using var pilot = await Pilot();
        await Accept(pilot, await Create(shop, app.CustomerA));
        var ready = await Create(shop, app.CustomerA);
        var scheduled = await Create(shop, app.CustomerA);
        var reserved = await Create(shop, app.CustomerA);
        var paused = await Create(shop, app.CustomerA);
        await app.InScopeAsync(async sp =>
        {
            var operations = sp.GetRequiredService<EnvirapidoOperationsService>();
            var manager = await operations.ManagerAsync("manager-a");
            await operations.GuardarDatosEnvioAsync(manager, scheduled.OrdenId,
                new(null, "Estandar", 1, null, 2, null, app.Clock.GetUtcNow().UtcDateTime.AddHours(1), null, null, Key()));
            await operations.CrearRutaAsync(manager, new("Ruta reservada", Today, [reserved.OrdenId], app.PilotA2, null, false, Key()));
            await operations.GuardarReglaIncidenciaAsync(manager, new("Bloqueo QA", 2, null, "PausarRuta", true, false, Key()));
            await operations.ReportarIncidenciaAsync(manager, new("Bloqueo QA", "Conservar pausa por orden", Key(), OrdenId: paused.OrdenId));
        });
        var offers = (await pilot.GetFromJsonAsync<List<OrdenAutoDisponibleDto>>("/api/piloto/ordenes-auto"))!;
        Assert.Equal(ready.OrdenId, Assert.Single(offers).OrdenId);
        foreach (var blocked in new[] { scheduled, reserved, paused })
            Assert.Equal(HttpStatusCode.Conflict, (await pilot.PostAsync($"/api/piloto/ordenes-auto/{blocked.OrdenId}/aceptar", null)).StatusCode);
        await Accept(pilot, ready);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            foreach (var blocked in new[] { scheduled, reserved, paused })
            {
                var row = (await db.OrdenesEntrega.FindAsync(blocked.OrdenId))!;
                Assert.Null(row.PilotoId);
                Assert.Equal(EstadoOrden.Creada, row.Estado);
                Assert.Equal(1, await db.HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == row.Id));
            }
            Assert.False(await db.CodigosEntrega.AnyAsync(c => c.Usado));
        });
    }

    [Fact]
    public async Task Busy_pilot_and_manager_cannot_cross_tenants_while_taking_additional_work()
    {
        using var shopA = await app.LoginAsync("shop-a");
        using var shopB = await app.LoginAsync("shop-b");
        using var pilot = await Pilot();
        var active = await Create(shopA, app.CustomerA);
        var next = await Create(shopA, app.CustomerA);
        var foreign = await Create(shopB, app.CustomerB);
        await Accept(pilot, active);
        var offers = (await pilot.GetFromJsonAsync<List<OrdenAutoDisponibleDto>>("/api/piloto/ordenes-auto"))!;
        Assert.Equal(next.OrdenId, Assert.Single(offers).OrdenId);
        Assert.Equal(HttpStatusCode.BadRequest, (await pilot.PostAsync($"/api/piloto/ordenes-auto/{foreign.OrdenId}/aceptar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pilot.PostAsync($"/api/piloto/ordenes-auto/{foreign.OrdenId}/rechazar", null)).StatusCode);
        await app.InScopeAsync(sp => Assert.ThrowsAsync<InvalidOperationException>(() =>
            sp.GetRequiredService<IOrdenService>().AsignarPilotoAsync(next.CodigoB, app.PilotB, "manager-a", "Gestor", next.OrdenId)));
        await Accept(pilot, next);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var row = (await db.OrdenesEntrega.FindAsync(foreign.OrdenId))!;
            Assert.Equal(app.TenantB, row.ClienteAppId);
            Assert.Null(row.PilotoId);
            Assert.Equal(EstadoOrden.Creada, row.Estado);
            Assert.Equal(1, await db.HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == row.Id));
            Assert.False(await db.OrdenesAutoRechazo.AnyAsync());
            Assert.False(await db.CodigosEntrega.AnyAsync(c => c.Usado));
        });
    }
}
