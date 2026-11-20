using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ManagedPilotFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value);
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private sealed record ManagedOrder(OrdenCreadaResult Order, RevisionParadaRequest[] First, RevisionParadaRequest[] Second);

    private async Task<ManagedOrder> CreateAsync(int first = 2, int second = 1)
    {
        ManagedOrder result = null!;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var secret = new UsuarioFinal { TiendaId = app.ShopA, Nombre = "OTHER-PILOT-SECRET", DireccionUbicacion = "OTHER-PRIVATE-ADDRESS", Telefono = "5550199" };
            db.UsuariosFinales.Add(secret); await db.SaveChangesAsync();
            var recipients = Enumerable.Range(0, first + second).Select(i => new OrdenDestinatarioRequest(i < first ? app.CustomerA : secret.Id)).ToArray();
            var order = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA, app.ShopA,
                recipients[0].UsuarioFinalId, operacionId: Key(), destinatarios: recipients);
            var entity = await db.OrdenesEntrega.Include(o => o.Paradas).SingleAsync(o => o.Id == order.OrdenId);
            entity.GestionPorParadas = true; entity.EsAuto = false; entity.PilotoId = null; entity.Estado = EstadoOrden.Asignada;
            var stops = entity.Paradas.OrderBy(p => p.Posicion).ToArray();
            for (var i = 0; i < stops.Length; i++)
            { stops[i].PilotoAsignadoId = i < first ? app.PilotA : app.PilotA2; stops[i].RevisionAsignacion = Key(); }
            await db.SaveChangesAsync();
            result = new(order, stops.Take(first).Select(p => new RevisionParadaRequest(p.Id, p.RevisionAsignacion)).ToArray(),
                stops.Skip(first).Select(p => new RevisionParadaRequest(p.Id, p.RevisionAsignacion)).ToArray());
        });
        return result;
    }

    private async Task Pickup(ManagedOrder order, string actor, IReadOnlyList<RevisionParadaRequest> stops)
        => await app.InScopeAsync(async sp => { await sp.GetRequiredService<IOrdenService>().ConfirmarRecogidaParadasAsync(
            order.Order.OrdenId, order.Order.CodigoB, stops, actor, Key()); });
    private async Task<EntregaParadaResult> Deliver(ManagedOrder order, string actor, RevisionParadaRequest stop, string? key = null)
    {
        EntregaParadaResult ack = null!;
        var code = order.Order.Paradas.Single(p => p.ParadaId == stop.ParadaId).CodigoC;
        await app.InScopeAsync(async sp => ack = await sp.GetRequiredService<IOrdenService>().ConfirmarEntregaParadaAsync(
            order.Order.OrdenId, stop.ParadaId, code, actor, key ?? Key(), stop.RevisionAsignacion));
        return ack;
    }

    [Fact]
    public async Task Split_pickups_reveal_only_owned_stops_and_each_pilot_finishes_without_closing_the_other_work()
    {
        var order = await CreateAsync();
        using var first = await app.LoginAsync("pilot-a"); first.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "4");
        using var second = await app.LoginAsync("pilot-a2"); second.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "4");
        var before = (await first.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!.Single();
        Assert.True(before.GestionPorParadas); Assert.False(before.PuedeVerDestinoFinal); Assert.Equal(2, before.Paradas.Count);
        Assert.All(before.Paradas, p => { Assert.False(p.PuedeVerDestino); Assert.Empty(p.Nombre); Assert.Null(p.Direccion); Assert.NotNull(p.RevisionAsignacion); });
        Assert.DoesNotContain("OTHER-PILOT-SECRET", JsonSerializer.Serialize(before));
        Assert.DoesNotContain(before.Paradas, p => p.ParadaId == order.Second.Single().ParadaId);
        var pickupKey = Key();
        var pickupBody = new { codigoB = order.Order.CodigoB, operacionId = pickupKey, paradas = order.First };
        var picked = await first.PostAsJsonAsync($"/api/piloto/ordenes/{order.Order.OrdenId}/recoger-paradas", pickupBody);
        picked.EnsureSuccessStatusCode();
        var pickupAck = await picked.Content.ReadAsStringAsync();
        Assert.Equal(pickupAck, await (await first.PostAsJsonAsync($"/api/piloto/ordenes/{order.Order.OrdenId}/recoger-paradas", pickupBody)).Content.ReadAsStringAsync());
        var own = (await first.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!.Single();
        Assert.All(own.Paradas, p => Assert.True(p.PuedeVerDestino)); Assert.DoesNotContain("OTHER-PILOT-SECRET", JsonSerializer.Serialize(own));
        Assert.All((await second.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!.Single().Paradas, p => Assert.False(p.PuedeVerDestino));
        DateTime? firstB = null;
        await app.InScopeAsync(async sp => firstB = (await sp.GetRequiredService<ApplicationDbContext>().CodigosEntrega.AsNoTracking()
            .SingleAsync(c => c.OrdenEntregaId == order.Order.OrdenId && c.Tipo == TipoCodigo.B_Recoleccion)).UsadoAt);
        var keyC = Key(); var initial = await Deliver(order, "pilot-a", order.First[0], keyC);
        Assert.False(initial.TrabajoPilotoFinalizado); Assert.Equal(2, initial.TotalParadas); Assert.Equal(1, initial.Entregadas);
        var ownFinished = await Deliver(order, "pilot-a", order.First[1]);
        Assert.True(ownFinished.TrabajoPilotoFinalizado); Assert.Equal("Recolectada", ownFinished.EstadoOrden);
        Assert.Empty((await first.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!);
        var history = (await first.GetFromJsonAsync<PaginaOrdenesPilotoDto>("/api/piloto/historial"))!;
        Assert.Equal("Entregada", Assert.Single(history.Items).Estado); Assert.Equal(2, history.Items.Single().Paradas.Count);
        await Pickup(order, "pilot-a2", order.Second);
        var last = await Deliver(order, "pilot-a2", order.Second.Single());
        Assert.Equal("Entregada", last.EstadoOrden); Assert.True(last.TrabajoPilotoFinalizado);
        Assert.Equal(initial, await Deliver(order, "pilot-a", order.First[0], keyC));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(firstB, (await db.CodigosEntrega.AsNoTracking().SingleAsync(c => c.OrdenEntregaId == order.Order.OrdenId && c.Tipo == TipoCodigo.B_Recoleccion)).UsadoAt);
            Assert.All(await db.CodigosEntrega.AsNoTracking().Where(c => c.OrdenEntregaId == order.Order.OrdenId).ToListAsync(), c => { Assert.True(c.Usado); Assert.False(c.Reservado); });
        });
    }

    [Fact]
    public async Task Legacy_endpoints_cannot_pick_up_or_close_managed_work_and_cap3_does_not_receive_it()
    {
        var order = await CreateAsync(1, 0);
        using var old = await app.LoginAsync("pilot-a"); old.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "3");
        Assert.Empty((await old.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await old.GetAsync($"/api/piloto/ordenes/{order.Order.OrdenId}")).StatusCode);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await old.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { ordenId = order.Order.OrdenId, codigoB = order.Order.CodigoB, operacionId = Key() })).StatusCode);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await old.PostAsJsonAsync($"/api/piloto/ordenes/{order.Order.OrdenId}/paradas/{order.First[0].ParadaId}/confirmar-entrega", new { codigoC = order.Order.CodigoC, operacionId = Key() })).StatusCode);
        await app.InScopeAsync(async sp => await Assert.ThrowsAsync<ArgumentException>(() =>
            sp.GetRequiredService<IOrdenService>().ConfirmarEntregaParadaAsync(order.Order.OrdenId,
                order.First[0].ParadaId, order.Order.CodigoC!, "pilot-a", null, order.First[0].RevisionAsignacion)));
        await app.InScopeAsync(async sp => Assert.All(await sp.GetRequiredService<ApplicationDbContext>().CodigosEntrega.AsNoTracking()
            .Where(c => c.OrdenEntregaId == order.Order.OrdenId).ToListAsync(), c => Assert.False(c.Usado)));
    }

    [Fact]
    public async Task Changed_assignment_and_foreign_custody_reject_stale_C_and_pickup_even_with_a_stale_tracker()
    {
        var order = await CreateAsync(1, 0); await Pickup(order, "pilot-a", order.First);
        using var originalScope = app.Services.CreateScope();
        var originalDb = originalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cached = await originalDb.ParadasEntrega.SingleAsync(p => p.Id == order.First[0].ParadaId);
        var revision = Key();
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            await using var tx = await sp.GetRequiredService<QuotaPolicyService>().BeginTenantMutationAsync(app.TenantA);
            await db.ParadasEntrega.Where(p => p.Id == cached.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.PilotoAsignadoId, app.PilotA2)
                .SetProperty(p => p.RevisionAsignacion, revision).SetProperty(p => p.RecogidaParadaAt, (DateTime?)null));
            await tx.CommitAsync();
        });
        var core = originalScope.ServiceProvider.GetRequiredService<IOrdenService>();
        var stale = await Assert.ThrowsAsync<RejectedOperationException>(() => core.ConfirmarEntregaParadaAsync(order.Order.OrdenId, cached.Id,
            order.Order.CodigoC!, "pilot-a", Key(), order.First[0].RevisionAsignacion)); Assert.Equal(409, stale.StatusCode);
        var receiver = new RevisionParadaRequest(cached.Id, revision);
        await Assert.ThrowsAsync<RejectedOperationException>(() => core.ConfirmarRecogidaParadasAsync(order.Order.OrdenId,
            order.Order.CodigoB, [receiver], "pilot-a2", Key()));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.False((await db.CodigosEntrega.AsNoTracking().SingleAsync(c => c.ParadaEntregaId == cached.Id)).Usado);
            await using var tx = await sp.GetRequiredService<QuotaPolicyService>().BeginTenantMutationAsync(app.TenantA);
            await db.ParadasEntrega.Where(p => p.Id == cached.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.PilotoCustodioId, app.PilotA2)
                .SetProperty(p => p.RecogidaParadaAt, DateTime.UtcNow)); await tx.CommitAsync();
        });
        await core.ConfirmarEntregaParadaAsync(order.Order.OrdenId, cached.Id, order.Order.CodigoC!, "pilot-a2", Key(), revision);
        Assert.Equal(app.PilotA2, (await originalDb.ParadasEntrega.AsNoTracking().SingleAsync(p => p.Id == cached.Id)).PilotoEntregaId);
    }

    private async Task<int> AddRoute(ManagedOrder order, bool pauseSecond = false)
    {
        var id = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var route = new RutaOperativa { ClienteAppId = app.TenantA, PilotoId = app.PilotA, Estado = EstadoRutaOperativa.Activa,
                Nombre = "Scoped pickup batch", Fecha = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-4)), IniciadaAt = DateTime.UtcNow };
            var position = 1;
            foreach (var stop in order.First)
            {
                var member = new RutaOrdenOperativa { ClienteAppId = app.TenantA, OrdenEntregaId = order.Order.OrdenId, ParadaEntregaId = stop.ParadaId };
                route.Ordenes.Add(member);
                route.Puntos.Add(new() { ClienteAppId = app.TenantA, OrdenEntregaId = order.Order.OrdenId, ParadaEntregaId = stop.ParadaId,
                    RutaOrden = member, Tipo = TipoPuntoRuta.Recogida, Posicion = position++ });
            }
            foreach (var stop in order.First)
                route.Puntos.Add(new() { ClienteAppId = app.TenantA, OrdenEntregaId = order.Order.OrdenId, ParadaEntregaId = stop.ParadaId,
                    RutaOrden = route.Ordenes.Single(m => m.ParadaEntregaId == stop.ParadaId), Tipo = TipoPuntoRuta.Entrega, Posicion = position++ });
            db.RutasOperativas.Add(route);
            if (pauseSecond) db.IncidenciasOperativas.Add(new() { ClienteAppId = app.TenantA, OrdenEntregaId = order.Order.OrdenId,
                ParadaEntregaId = order.First[1].ParadaId, Categoria = "Pause second", Descripcion = "Batch rollback fixture", Efecto = EfectoIncidencia.PausarParada });
            await db.SaveChangesAsync(); id = route.Id;
        }); return id;
    }

    [Fact]
    public async Task A_late_guard_failure_rolls_back_the_entire_pickup_batch_and_preserves_route_pickup_snapshots()
    {
        var order = await CreateAsync(2, 0); var route = await AddRoute(order, true); var key = Key();
        await app.InScopeAsync(async sp =>
        {
            var core = sp.GetRequiredService<IOrdenService>();
            await Assert.ThrowsAsync<OperationsException>(() => core.ConfirmarRecogidaParadasAsync(order.Order.OrdenId, order.Order.CodigoB, order.First, "pilot-a", key));
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.All(await db.ParadasEntrega.AsNoTracking().Where(p => p.OrdenEntregaId == order.Order.OrdenId).ToListAsync(), p => Assert.Null(p.RecogidaParadaAt));
            Assert.All(await db.PuntosRutaOperativos.AsNoTracking().Where(p => p.RutaId == route).ToListAsync(), p => Assert.Null(p.RecogidaConfirmadaAt));
            Assert.False(await db.OperacionesOrden.AnyAsync(o => o.Clave == key));
            await db.IncidenciasOperativas.Where(i => i.OrdenEntregaId == order.Order.OrdenId).ExecuteUpdateAsync(s => s.SetProperty(i => i.Estado, EstadoIncidencia.Resuelta));
            await core.ConfirmarRecogidaParadasAsync(order.Order.OrdenId, order.Order.CodigoB, order.First, "pilot-a", key);
            Assert.All(await db.PuntosRutaOperativos.AsNoTracking().Where(p => p.RutaId == route && p.Tipo == TipoPuntoRuta.Recogida).ToListAsync(), p => Assert.NotNull(p.RecogidaConfirmadaAt));
        });
        await Deliver(order, "pilot-a", order.First[0]); await Deliver(order, "pilot-a", order.First[1]);
        await app.InScopeAsync(async sp => Assert.Equal(EstadoRutaOperativa.Finalizada,
            (await sp.GetRequiredService<ApplicationDbContext>().RutasOperativas.AsNoTracking().SingleAsync(r => r.Id == route)).Estado));
    }

    [Fact]
    public async Task Completed_managed_route_pays_only_its_executor_stops_while_another_pilot_keeps_the_parent_open()
    {
        var order = await CreateAsync(); var route = await AddRoute(order);
        await Pickup(order, "pilot-a", order.First); await Deliver(order, "pilot-a", order.First[0]); await Deliver(order, "pilot-a", order.First[1]);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Recolectada, (await db.OrdenesEntrega.AsNoTracking().SingleAsync(o => o.Id == order.Order.OrdenId)).Estado);
            Assert.Equal(EstadoRutaOperativa.Finalizada, (await db.RutasOperativas.AsNoTracking().SingleAsync(r => r.Id == route)).Estado);
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            var rule = Ack(await service.GuardarReglaRemuneracionAsync(manager, new("Managed route fixed", "Ruta", 30m, 0m, 0m, 0m, true, Key()))).GetProperty("id").GetInt32();
            var day = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-4));
            var paid = Ack(await service.GenerarLiquidacionAsync(manager, new(day, day, Key(), PilotoId: app.PilotA, ReglaId: rule)));
            Assert.Equal(30m, paid.GetProperty("neto").GetDecimal());
            var coverage = await db.MovimientosOperativos.AsNoTracking().Where(m => m.Concepto == "Cobertura").Select(m => m.ParadaEntregaId).ToListAsync();
            Assert.Equal(order.First.Select(p => (int?)p.ParadaId).Order(), coverage.Order());
            Assert.DoesNotContain((int?)order.Second.Single().ParadaId, coverage);
        });
    }

    [Fact]
    public async Task Concurrent_C_commands_consume_one_stop_once_and_freeze_its_executor()
    {
        var order = await CreateAsync(1, 0); await Pickup(order, "pilot-a", order.First);
        var ready = 0; var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Confirm()
        {
            using var scope = app.Services.CreateScope();
            if (Interlocked.Increment(ref ready) == 2) gate.SetResult();
            await gate.Task;
            try { await scope.ServiceProvider.GetRequiredService<IOrdenService>().ConfirmarEntregaParadaAsync(order.Order.OrdenId,
                order.First[0].ParadaId, order.Order.CodigoC!, "pilot-a", Key(), order.First[0].RevisionAsignacion); return true; }
            catch (RejectedOperationException) { return false; }
        }
        Assert.Single((await Task.WhenAll(Confirm(), Confirm())).Where(v => v));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(app.PilotA, (await db.ParadasEntrega.AsNoTracking().SingleAsync(p => p.Id == order.First[0].ParadaId)).PilotoEntregaId);
            Assert.Equal(1, await db.HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == order.Order.OrdenId && h.TipoEvento == TipoEventoOrden.Entregada));
        });
    }

    [Fact]
    public async Task Committed_legacy_pickup_and_C_receipts_survive_conversion_without_repeating_transitions()
    {
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var core = sp.GetRequiredService<IOrdenService>();
            var order = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA,
                destinatarios: [new(app.CustomerA), new(app.CustomerA)]);
            await core.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            var pickupKey = Key(); var deliveryKey = Key();
            await core.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, pickupKey);
            var first = order.Paradas[0];
            var original = await core.ConfirmarEntregaParadaAsync(order.OrdenId, first.ParadaId, first.CodigoC, "pilot-a", deliveryKey);
            var usedB = (await db.CodigosEntrega.AsNoTracking().SingleAsync(c => c.OrdenEntregaId == order.OrdenId && c.Tipo == TipoCodigo.B_Recoleccion)).UsadoAt;
            db.ChangeTracker.Clear();
            // Fixture conversion representing an already received split; the new
            // reassignment command itself is covered by ReasignacionFlowTests.
            var converted = await db.OrdenesEntrega.Include(o => o.Paradas).SingleAsync(o => o.Id == order.OrdenId);
            converted.GestionPorParadas = true; converted.PilotoId = null; converted.EsAuto = false;
            foreach (var stop in converted.Paradas)
            {
                stop.RevisionAsignacion = Key(); stop.RecogidaParadaAt = converted.RecolectadaAt;
                stop.PilotoAsignadoId = stop.Id == first.ParadaId ? app.PilotA : app.PilotA2;
                stop.PilotoEntregaId = stop.Estado == EstadoParadaEntrega.Entregada ? app.PilotA : null;
                stop.PilotoCustodioId = stop.Estado == EstadoParadaEntrega.Entregada ? null : app.PilotA2;
            }
            await db.SaveChangesAsync();
            await core.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, pickupKey);
            Assert.Equal(original, await core.ConfirmarEntregaParadaAsync(order.OrdenId, first.ParadaId,
                first.CodigoC, "pilot-a", deliveryKey));
            await Assert.ThrowsAsync<OperationsException>(() => core.ConfirmarRecolectaAsync(order.CodigoB,
                "pilot-a", "Piloto", order.OrdenId, Key()));
            Assert.Equal(usedB, (await db.CodigosEntrega.AsNoTracking().SingleAsync(c => c.OrdenEntregaId == order.OrdenId && c.Tipo == TipoCodigo.B_Recoleccion)).UsadoAt);
            Assert.Equal(1, await db.HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == order.OrdenId && h.TipoEvento == TipoEventoOrden.Recolectada));
            Assert.Equal(1, await db.HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == order.OrdenId && h.TipoEvento == TipoEventoOrden.Entregada));
        });
    }

    [Fact]
    public async Task A_stop_pause_follows_the_package_between_routes_while_a_route_pause_stays_on_its_route()
    {
        var order = await CreateAsync(1, 1); var originalRoute = await AddRoute(order);
        await Pickup(order, "pilot-a", order.First);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var moved = await db.ParadasEntrega.SingleAsync(p => p.Id == order.First[0].ParadaId);
            moved.PilotoAsignadoId = app.PilotA2; moved.PilotoCustodioId = app.PilotA2;
            moved.RevisionAsignacion = Key(); moved.RecogidaParadaAt = DateTime.UtcNow;
            foreach (var member in await db.RutaOrdenesOperativas.Where(m => m.RutaId == originalRoute).ToListAsync()) member.Vigente = false;
            var oldRoute = await db.RutasOperativas.SingleAsync(r => r.Id == originalRoute);
            oldRoute.Estado = EstadoRutaOperativa.Finalizada; oldRoute.FinalizadaAt = DateTime.UtcNow;
            var route = new RutaOperativa { ClienteAppId = app.TenantA, PilotoId = app.PilotA2, Estado = EstadoRutaOperativa.Activa,
                Nombre = "Receiver route", Fecha = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-4)), IniciadaAt = DateTime.UtcNow };
            var slot = new RutaOrdenOperativa { ClienteAppId = app.TenantA, OrdenEntregaId = order.Order.OrdenId, ParadaEntregaId = moved.Id };
            route.Ordenes.Add(slot);
            route.Puntos.Add(new() { ClienteAppId = app.TenantA, OrdenEntregaId = order.Order.OrdenId, ParadaEntregaId = moved.Id,
                RutaOrden = slot, Tipo = TipoPuntoRuta.Entrega, Posicion = 1 });
            db.RutasOperativas.Add(route);
            var incident = new IncidenciaOperativa { ClienteAppId = app.TenantA, OrdenEntregaId = order.Order.OrdenId,
                ParadaEntregaId = moved.Id, RutaId = originalRoute, Efecto = EfectoIncidencia.PausarParada,
                Categoria = "Package hold", Descripcion = "Pause remains bound to this package" };
            db.IncidenciasOperativas.Add(incident); await db.SaveChangesAsync();
            var guard = new OperationsWorkGuard(db);
            await Assert.ThrowsAsync<OperationsException>(() => guard.ValidateAsync(app.TenantA, order.Order.OrdenId, moved.Id, false));
            Assert.Null((await guard.LoadReadPolicyAsync(app.TenantA, [order.Order.OrdenId]))
                .MotivoBloqueo(order.Order.OrdenId, order.Second.Single().ParadaId, false));
            incident.Efecto = EfectoIncidencia.PausarRuta; await db.SaveChangesAsync();
            await guard.ValidateAsync(app.TenantA, order.Order.OrdenId, moved.Id, false);
        });
    }
}
