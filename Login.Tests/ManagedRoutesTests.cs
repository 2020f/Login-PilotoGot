using System.Text.Json;
using System.Net.Http.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ManagedRoutesTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    private OperationsActor Manager => new(app.TenantA, "manager-a");
    private DateOnly Today => DateOnly.FromDateTime(app.Clock.GetUtcNow().UtcDateTime.AddHours(-4));
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    private async Task<OrdenCreadaResult> Create(int stops = 2, bool managed = true, bool split = true)
    {
        OrdenCreadaResult order = null!;
        await app.InScopeAsync(async sp =>
        {
            order = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA,
                operacionId: Key(), destinatarios: Enumerable.Range(0, stops).Select(_ => new OrdenDestinatarioRequest(app.CustomerA, null)).ToArray());
            if (!managed) return;
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var row = await db.OrdenesEntrega.Include(o => o.Paradas).SingleAsync(o => o.Id == order.OrdenId);
            row.GestionPorParadas = true; row.EsAuto = false; row.Estado = EstadoOrden.Asignada;
            row.PilotoId = split && stops > 1 ? null : app.PilotA;
            foreach (var stop in row.Paradas)
            {
                stop.Nombre = $"Destino privado de parada {stop.Id}";
                stop.PilotoAsignadoId = split && stop.Posicion > 1 ? app.PilotA2 : app.PilotA;
                stop.RevisionAsignacion = Key();
            }
            await db.SaveChangesAsync();
        });
        return order;
    }

    private async Task<JsonElement> Route(OrdenCreadaResult order, int pilot, params int[] stops)
    {
        JsonElement result = default;
        await app.InScopeAsync(async sp => result = Ack(await sp.GetRequiredService<EnvirapidoOperationsService>().CrearRutaAsync(Manager,
            new("Ruta por paradas QA", Today, [order.OrdenId], pilot, null, false, Key(), stops.ToList()))));
        return result;
    }

    private async Task<JsonElement> State(JsonElement route, string action)
    {
        JsonElement result = default;
        await app.InScopeAsync(async sp => result = Ack(await sp.GetRequiredService<EnvirapidoOperationsService>().EstadoRutaAsync(Manager,
            route.GetProperty("id").GetInt32(), new(action, route.GetProperty("version").GetString()!, Key()))));
        return result;
    }

    [Fact]
    public async Task One_order_can_have_routes_for_distinct_stops_without_disclosing_the_other_pilots_destinations()
    {
        var order = await Create();
        var first = await State(await Route(order, app.PilotA, order.Paradas[0].ParadaId), "Iniciar");
        await State(await Route(order, app.PilotA2, order.Paradas[1].ParadaId), "Iniciar");
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var stop = await db.ParadasEntrega.SingleAsync(p => p.Id == order.Paradas[0].ParadaId);
            stop.RecogidaParadaAt = app.Clock.GetUtcNow().UtcDateTime; stop.PilotoCustodioId = app.PilotA;
            var pickup = await db.PuntosRutaOperativos.SingleAsync(p => p.RutaId == first.GetProperty("id").GetInt32() && p.Tipo == TipoPuntoRuta.Recogida);
            pickup.RecogidaConfirmadaAt = stop.RecogidaParadaAt;
            await db.SaveChangesAsync();
        });
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var firstSnapshot = await service.SnapshotAsync(new(app.TenantA, "pilot-a", app.PilotA));
            var one = Assert.Single(firstSnapshot.Rutas);
            Assert.Equal(1, firstSnapshot.Indicadores!.ParadasPendientes);
            Assert.All(one.Puntos, p => Assert.Equal(order.Paradas[0].ParadaId, p.ParadaId));
            Assert.Contains(one.Puntos, p => p.Tipo == "Entrega" && p.PuedeVerDestino && p.Nombre.Contains($"{order.Paradas[0].ParadaId}"));
            Assert.DoesNotContain($"Destino privado de parada {order.Paradas[1].ParadaId}", JsonSerializer.Serialize(one));
            var secondSnapshot = await service.SnapshotAsync(new(app.TenantA, "pilot-a2", app.PilotA2));
            var two = Assert.Single(secondSnapshot.Rutas);
            Assert.Equal(1, secondSnapshot.Indicadores!.ParadasPendientes);
            Assert.All(two.Puntos.Where(p => p.Tipo == "Entrega"), p => Assert.False(p.PuedeVerDestino));
            Assert.Equal(2, await sp.GetRequiredService<ApplicationDbContext>().RutaOrdenesOperativas.CountAsync(m => m.Vigente));
            // The same order now occurs in two routes; tracking must not build a dictionary by order alone.
            await service.SeguimientoPedidoAsync(Manager, order.OrdenId, order.Paradas[0].ParadaId);
        });
        using var manager = await app.LoginWebAsync("manager-a");
        var map = await manager.GetFromJsonAsync<JsonElement>("/Operaciones/MapaDatos");
        var fleet = map.GetProperty("pilotos").EnumerateArray().ToList();
        Assert.Equal(1, fleet.Single(p => p.GetProperty("id").GetInt32() == app.PilotA).GetProperty("carga").GetInt32());
        Assert.Equal(1, fleet.Single(p => p.GetProperty("id").GetInt32() == app.PilotA2).GetProperty("carga").GetInt32());
        Assert.DoesNotContain(fleet, p => p.GetProperty("id").GetInt32() == app.PilotB);
    }

    [Fact]
    public async Task Route_reservation_rejects_duplicate_units_and_pending_physical_handoffs()
    {
        var order = await Create();
        await Route(order, app.PilotA, order.Paradas[0].ParadaId);
        await Assert.ThrowsAsync<RejectedOperationException>(() => Route(order, app.PilotA, order.Paradas[0].ParadaId));
        await Assert.ThrowsAsync<RejectedOperationException>(() => Route(order, app.PilotB, order.Paradas[1].ParadaId));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var pending = await db.ParadasEntrega.SingleAsync(p => p.Id == order.Paradas[1].ParadaId);
            pending.PilotoCustodioId = app.PilotA; pending.RecogidaParadaAt = null;
            await db.SaveChangesAsync();
        });
        await Assert.ThrowsAsync<RejectedOperationException>(() => Route(order, app.PilotA2, order.Paradas[1].ParadaId));
        await app.InScopeAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<ApplicationDbContext>().RutasOperativas.CountAsync()));
    }

    [Fact]
    public async Task Cancelling_an_uncollected_route_preserves_another_pilots_collected_stop_of_the_same_order()
    {
        var order = await Create();
        var route = await State(await Route(order, app.PilotA, order.Paradas[0].ParadaId), "Iniciar");
        await State(await Route(order, app.PilotA2, order.Paradas[1].ParadaId), "Iniciar");
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var parent = await db.OrdenesEntrega.Include(o => o.Paradas).SingleAsync(o => o.Id == order.OrdenId);
            parent.RecolectadaAt = app.Clock.GetUtcNow().UtcDateTime; parent.Estado = EstadoOrden.Recolectada;
            var other = parent.Paradas.Single(p => p.Id == order.Paradas[1].ParadaId);
            other.RecogidaParadaAt = parent.RecolectadaAt; other.PilotoCustodioId = app.PilotA2;
            await db.SaveChangesAsync();
        });
        await State(route, "Cancelar");
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var parent = await db.OrdenesEntrega.Include(o => o.Paradas).SingleAsync(o => o.Id == order.OrdenId);
            Assert.Null(parent.Paradas.Single(p => p.Id == order.Paradas[0].ParadaId).PilotoAsignadoId);
            var other = parent.Paradas.Single(p => p.Id == order.Paradas[1].ParadaId);
            Assert.Equal(app.PilotA2, other.PilotoAsignadoId); Assert.Equal(app.PilotA2, other.PilotoCustodioId);
            Assert.NotNull(other.RecogidaParadaAt); Assert.Equal(EstadoOrden.Recolectada, parent.Estado);
            Assert.Single(await db.RutaOrdenesOperativas.Where(m => m.Vigente).ToListAsync());
        });
    }

    [Fact]
    public async Task A_route_finishes_its_own_deliveries_while_other_stops_of_the_order_remain_active()
    {
        var order = await Create();
        var route = await State(await Route(order, app.PilotA, order.Paradas[0].ParadaId), "Iniciar");
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var own = await db.ParadasEntrega.SingleAsync(p => p.Id == order.Paradas[0].ParadaId);
            own.RecogidaParadaAt = app.Clock.GetUtcNow().UtcDateTime; own.Estado = EstadoParadaEntrega.Entregada;
            own.EntregadaAt = own.RecogidaParadaAt; own.PilotoEntregaId = app.PilotA;
            (await db.PuntosRutaOperativos.SingleAsync(p => p.RutaId == route.GetProperty("id").GetInt32() && p.Tipo == TipoPuntoRuta.Recogida)).RecogidaConfirmadaAt = own.RecogidaParadaAt;
            await db.SaveChangesAsync();
        });
        await State(route, "Finalizar");
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoRutaOperativa.Finalizada, (await db.RutasOperativas.SingleAsync()).Estado);
            Assert.Equal(EstadoParadaEntrega.Pendiente, (await db.ParadasEntrega.SingleAsync(p => p.Id == order.Paradas[1].ParadaId)).Estado);
            Assert.Null((await db.OrdenesEntrega.SingleAsync()).FinalizadaAt);
        });
    }

    [Fact]
    public async Task Mixed_routes_keep_each_membership_pickup_before_its_delivery_when_reordering()
    {
        var managed = await Create(split: false); var legacy = await Create(1, managed: false);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var route = Ack(await service.CrearRutaAsync(Manager, new("Ruta mixta", Today, [legacy.OrdenId, managed.OrdenId], app.PilotA, null, false, Key(), managed.Paradas.Select(p => p.ParadaId).ToList())));
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var points = await db.PuntosRutaOperativos.Where(p => p.RutaId == route.GetProperty("id").GetInt32()).ToListAsync();
            Assert.Equal(6, points.Count);
            var sequence = points.GroupBy(p => p.RutaOrdenId).SelectMany(g => g.OrderBy(p => p.Tipo == TipoPuntoRuta.Recogida ? 0 : 1)).Select(p => p.Id).ToList();
            var changed = Ack(await service.ReordenarRutaAsync(Manager, route.GetProperty("id").GetInt32(), new(sequence, route.GetProperty("version").GetString()!, Key())));
            (sequence[0], sequence[1]) = (sequence[1], sequence[0]);
            await Assert.ThrowsAsync<RejectedOperationException>(() => service.ReordenarRutaAsync(Manager, route.GetProperty("id").GetInt32(), new(sequence, changed.GetProperty("version").GetString()!, Key())));
        });
    }

    [Fact]
    public async Task Converting_a_collected_legacy_order_and_retiring_one_stop_preserves_other_route_work_and_pickup_evidence()
    {
        var legacy = await Create(2, managed: false); var other = await Create(1, managed: false);
        int routeId = 0; int[] pointIds = [];
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var route = Ack(await service.CrearRutaAsync(Manager, new("Ruta heredada", Today, [legacy.OrdenId, other.OrdenId], app.PilotA, null, false, Key())));
            routeId = route.GetProperty("id").GetInt32();
            await service.EstadoRutaAsync(Manager, routeId, new("Iniciar", route.GetProperty("version").GetString()!, Key()));
        });
        await app.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IOrdenService>().ConfirmarRecolectaAsync(legacy.CodigoB, "pilot-a", "Piloto", legacy.OrdenId, Key());
            pointIds = await sp.GetRequiredService<ApplicationDbContext>().PuntosRutaOperativos.Where(p => p.RutaId == routeId).OrderBy(p => p.Id).Select(p => p.Id).ToArrayAsync();
        });
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            await service.SolicitarRetiroParadasAsync(new(app.TenantA, "pilot-a", app.PilotA), new(legacy.OrdenId, [new(legacy.Paradas[0].ParadaId, "")], "Revisión de una parada", Key()));
        });
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var item = Assert.Single((await service.ReasignacionesAsync(Manager, legacy.OrdenId)).Gestiones);
            await service.DecidirRetiroParadaAsync(Manager, item.Id, new("Aprobar", "Retirar sólo esta parada", item.Version, Key()));
        });
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(pointIds, await db.PuntosRutaOperativos.Where(p => p.RutaId == routeId).OrderBy(p => p.Id).Select(p => p.Id).ToArrayAsync());
            Assert.True(await db.RutaOrdenesOperativas.AnyAsync(m => m.OrdenEntregaId == other.OrdenId && m.Vigente && m.ParadaEntregaId == null));
            Assert.True(await db.RutaOrdenesOperativas.AnyAsync(m => m.ParadaEntregaId == legacy.Paradas[1].ParadaId && m.Vigente));
            Assert.False(await db.RutaOrdenesOperativas.AnyAsync(m => m.ParadaEntregaId == legacy.Paradas[0].ParadaId && m.Vigente));
            var route = Assert.Single((await sp.GetRequiredService<EnvirapidoOperationsService>().SnapshotAsync(new(app.TenantA, "pilot-a", app.PilotA))).Rutas);
            Assert.Contains(route.Puntos, p => p.ParadaId == legacy.Paradas[0].ParadaId && p.Tipo == "Recogida" && p.Completado);
            Assert.Equal(app.PilotA, (await db.OrdenesEntrega.FindAsync(other.OrdenId))!.PilotoId);
            Assert.Equal(EstadoRutaOperativa.Activa, (await db.RutasOperativas.FindAsync(routeId))!.Estado);
        });
    }
}
