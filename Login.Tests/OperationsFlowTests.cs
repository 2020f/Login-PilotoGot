using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class OperationsFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private static string Key() => Guid.NewGuid().ToString("N");
    private static JsonElement Ack(object result) => JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")).DateTime);
    private async Task<OrdenCreadaResult> Create(bool secondShop = false)
    {
        OrdenCreadaResult result = null!;
        await app.InScopeAsync(async sp => result = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA,
            secondShop ? app.ShopA2 : app.ShopA, secondShop ? app.CustomerA2 : app.CustomerA, operacionId: Key()));
        return result;
    }
    private async Task Assign(OrdenCreadaResult o)
        => await app.InScopeAsync(async sp => await sp.GetRequiredService<IOrdenService>().AsignarPilotoAsync(o.CodigoB, app.PilotA, "manager-a", "Gestor", o.OrdenId));
    private async Task Pickup(OrdenCreadaResult o)
        => await app.InScopeAsync(async sp => await sp.GetRequiredService<IOrdenService>().ConfirmarRecolectaAsync(o.CodigoB, "pilot-a", "Piloto", o.OrdenId, Key()));
    private async Task Deliver(OrdenCreadaResult o)
        => await app.InScopeAsync(async sp => await sp.GetRequiredService<IOrdenService>().ConfirmarEntregaParadaAsync(o.OrdenId, o.Paradas.Single().ParadaId, o.Paradas.Single().CodigoC, "pilot-a", Key()));

    [Fact]
    public async Task Route_keeps_pickups_private_and_closes_only_after_all_orders_with_original_sessions()
    {
        var one = await Create(); var two = await Create(true);
        var routeId = 0;
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var manager = await service.ManagerAsync("manager-a");
            var request = new CrearRutaRequest("Ruta QA", Today, [one.OrdenId, two.OrdenId], app.PilotA, null, true, Key());
            var ack = Ack(await service.CrearRutaAsync(manager, request)); routeId = ack.GetProperty("id").GetInt32();
            Assert.Equal(routeId, Ack(await service.CrearRutaAsync(manager, request)).GetProperty("id").GetInt32());
            var planned = Assert.Single((await service.SnapshotAsync(await service.PilotAsync("pilot-a"))).Rutas);
            Assert.Equal("Planificada", planned.Estado);
            Assert.All(planned.Puntos.Where(p => p.Tipo == "Entrega"), p => { Assert.False(p.PuedeVerDestino); Assert.Null(p.Direccion); Assert.Null(p.Latitud); });
            await service.EstadoRutaAsync(manager, routeId, new("Iniciar", planned.Version, Key()));
        });
        Assert.Equal(409, (await Assert.ThrowsAsync<OperationsException>(() => Pickup(two))).StatusCode);
        await Pickup(one); await Pickup(two); await Deliver(one);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoRutaOperativa.Activa, (await db.RutasOperativas.FindAsync(routeId))!.Estado);
        });
        await Deliver(two);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoRutaOperativa.Finalizada, (await db.RutasOperativas.FindAsync(routeId))!.Estado);
            Assert.False(await db.RutaOrdenesOperativas.AnyAsync(x => x.Vigente));
            Assert.Equal(0, await db.MovimientosOperativos.CountAsync());
        });
    }

    [Fact]
    public async Task Cancelled_route_cannot_disclose_a_destination_after_reassignment_and_pickup_by_another_pilot()
    {
        var order = await Create();
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            var ack = Ack(await service.CrearRutaAsync(manager, new("Ruta retirada", Today, [order.OrdenId], app.PilotA, null, false, Key())));
            var routeId = ack.GetProperty("id").GetInt32();
            var started = Ack(await service.EstadoRutaAsync(manager, routeId, new("Iniciar", ack.GetProperty("version").GetString()!, Key())));
            await service.EstadoRutaAsync(manager, routeId, new("Cancelar", started.GetProperty("version").GetString()!, Key()));
        });
        await app.InScopeAsync(async sp =>
        {
            var orders = sp.GetRequiredService<IOrdenService>();
            await orders.AsignarPilotoAsync(order.CodigoB, app.PilotA2, "manager-a", "Gestor", order.OrdenId);
            await orders.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a2", "Piloto", order.OrdenId, Key());
        });
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var oldRoute = Assert.Single((await service.SnapshotAsync(await service.PilotAsync("pilot-a"))).Rutas);
            Assert.Equal("Cancelada", oldRoute.Estado); Assert.Null(oldRoute.Geometria);
            Assert.All(oldRoute.Puntos, p => { Assert.False(p.PuedeVerDestino); Assert.Null(p.Direccion); Assert.Null(p.Latitud); Assert.Null(p.Longitud); });
        });
    }

    [Fact]
    public async Task Incident_pause_and_resolution_preserve_delivery_and_do_not_replace_C()
    {
        var order = await Create(); await Assign(order); await Pickup(order);
        var id = 0;
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            await service.GuardarReglaIncidenciaAsync(manager, new("Sin contacto", 3, 15, "PausarParada", true, false, Key()));
            var request = new ReportarIncidenciaRequest("Sin contacto", "Destinatario no responde", Key(), order.OrdenId, order.Paradas.Single().ParadaId);
            id = Ack(await service.ReportarIncidenciaAsync(await service.PilotAsync("pilot-a"), request)).GetProperty("id").GetInt32();
            Assert.Equal(id, Ack(await service.ReportarIncidenciaAsync(await service.PilotAsync("pilot-a"), request)).GetProperty("id").GetInt32());
        });
        Assert.Equal(409, (await Assert.ThrowsAsync<OperationsException>(() => Deliver(order))).StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            var incident = Assert.Single((await service.SnapshotAsync(manager)).Incidencias);
            await Assert.ThrowsAsync<OperationsException>(() => service.CambiarIncidenciaAsync(new(app.TenantA, "pilot-a", app.PilotA), id, new("Resolver", "No autorizado", incident.Version, Key())));
            await service.CambiarIncidenciaAsync(manager, id, new("Resolver", "Destinatario confirmado por operador", incident.Version, Key()));
            Assert.Equal(0, await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega.CountAsync(p => p.EntregadaAt != null));
        });
        await Deliver(order);
    }

    [Fact]
    public async Task Payroll_is_frozen_not_duplicated_and_scheduling_does_not_confirm_external_payment()
    {
        var order = await Create(); await Assign(order); await Pickup(order); await Deliver(order);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            var rule = Ack(await service.GuardarReglaRemuneracionAsync(manager, new("Servicio QA", "Servicio", 100m, 0m, 10m, 10m, true, Key()))).GetProperty("id").GetInt32();
            var r = new GenerarLiquidacionRequest(Today, Today, Key(), PilotoId: app.PilotA, ReglaId: rule);
            var id = Ack(await service.GenerarLiquidacionAsync(manager, r)).GetProperty("id").GetInt32();
            Assert.Equal(id, Ack(await service.GenerarLiquidacionAsync(manager, r)).GetProperty("id").GetInt32());
            await Assert.ThrowsAsync<RejectedOperationException>(() => service.GenerarLiquidacionAsync(manager, r with { OperacionId = Key() }));
            var l = Assert.Single((await service.SnapshotAsync(manager)).Liquidaciones);
            Assert.Equal(100m, l.Neto); Assert.Equal(100m, l.Servicios); Assert.Equal(10m, l.Incentivos); Assert.Equal(10m, l.Comisiones);
            var approved = Ack(await service.CambiarLiquidacionAsync(manager, id, new("Aprobar", l.Version, Key())));
            var scheduled = Ack(await service.CambiarLiquidacionAsync(manager, id, new("Programar", approved.GetProperty("version").GetString()!, Key(), "Transferencia", "1234", Today.AddDays(1))));
            l = Assert.Single((await service.SnapshotAsync(manager)).Liquidaciones); Assert.Equal("Programada", l.Estado); Assert.Null(l.PagadaAt);
            app.Clock.Advance(TimeSpan.FromDays(2));
            Assert.Null(Assert.Single((await service.SnapshotAsync(manager)).Liquidaciones).PagadaAt);
            await service.CambiarLiquidacionAsync(manager, id, new("RegistrarPago", scheduled.GetProperty("version").GetString()!, Key(), "Transferencia", "1234", Today.AddDays(-1), "QA-PAGO-1"));
            l = Assert.Single((await service.SnapshotAsync(manager)).Liquidaciones);
            Assert.Equal("Pagada", l.Estado); Assert.NotNull(l.PagadaAt); Assert.Equal("•••• 1234", l.CuentaDestino);
            Assert.Equal(100m, l.Neto);
        });
    }

    [Fact]
    public async Task Cash_is_verified_and_received_by_company_without_reducing_pilot_earnings()
    {
        await app.InScopeAsync(async sp => { var db = sp.GetRequiredService<ApplicationDbContext>(); (await db.ClientesApp.FindAsync(app.TenantA))!.PrecioBase = 10m; await db.SaveChangesAsync(); });
        var order = await Create(); await Assign(order); await Pickup(order);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a"); var pilot = await service.PilotAsync("pilot-a");
            var cash = Ack(await service.RegistrarCobroAsync(pilot, new(order.OrdenId, 500m, "QA-COBRO-1", Key(), order.Paradas.Single().ParadaId)));
            var id = cash.GetProperty("id").GetInt32();
            await Assert.ThrowsAsync<OperationsException>(() => service.RecibirEfectivoAsync(pilot, id, new(500m, "QA-RECIBO", cash.GetProperty("version").GetString()!, Key())));
            var verified = Ack(await service.ConciliarCobroAsync(manager, id, new(480m, "Diferencia registrada y verificada", cash.GetProperty("version").GetString()!, Key())));
            await service.RecibirEfectivoAsync(manager, id, new(480m, "QA-RECIBO", verified.GetProperty("version").GetString()!, Key()));
            var row = Assert.Single((await service.SnapshotAsync(pilot)).Cobros);
            Assert.Equal(500m, row.Importe); Assert.Equal(480m, row.ImporteVerificado); Assert.Equal(480m, row.ImporteEntregadoEmpresa); Assert.NotNull(row.EntregadoEmpresaAt);
        });
        await Deliver(order);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            var rule = Ack(await service.GuardarReglaRemuneracionAsync(manager, new("Fijo QA", "Servicio", 100m, 0m, 0m, 0m, true, Key()))).GetProperty("id").GetInt32();
            await service.GenerarLiquidacionAsync(manager, new(Today, Today, Key(), PilotoId: app.PilotA, ReglaId: rule));
            await service.GenerarLiquidacionAsync(manager, new(Today, Today, Key(), TiendaId: app.ShopA));
            var rows = (await service.SnapshotAsync(manager)).Liquidaciones;
            Assert.Equal(100m, rows.Single(l => l.PilotoId.HasValue).Neto);
            Assert.Equal(470m, rows.Single(l => l.TiendaId.HasValue).Neto);
            Assert.Equal(0m, rows.Single(l => l.PilotoId.HasValue).EfectivoCompensado);
        });
    }

    [Fact]
    public async Task GPS_keeps_capture_time_and_old_batches_cannot_replace_latest_position()
    {
        var order = await Create(); await Assign(order);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var pilot = await service.PilotAsync("pilot-a"); var device = Key(); var now = app.Clock.GetUtcNow().UtcDateTime;
            var request = new EnviarGpsRequest(device, [new(18.48, -69.91, now, 2, 12)], Key());
            await service.EnviarGpsAsync(pilot, request); await service.EnviarGpsAsync(pilot, request);
            await service.EnviarGpsAsync(pilot, new(device, [new(18.10, -69.10, now.AddMinutes(-1), 1, 15)], Key()));
            var db = sp.GetRequiredService<ApplicationDbContext>(); var current = (await db.Pilotos.FindAsync(app.PilotA))!;
            Assert.Equal(18.48, current.Latitud); Assert.Equal(-69.91, current.Longitud); Assert.Equal(2, await db.PosicionesPilotosOperativas.CountAsync());
            var snapshot = await service.SnapshotAsync(pilot); Assert.True(snapshot.RequiereGps); Assert.NotNull(snapshot.AutorizacionGpsHasta);
            var other = await service.PilotAsync("pilot-b");
            await Assert.ThrowsAsync<OperationsException>(() => service.EnviarGpsAsync(other, new(device, [new(18.48, -69.91, now, 3)], Key())));
        });
    }

    [Fact]
    public async Task Scheduled_orders_are_pending_until_ready_and_cannot_be_taken_early()
    {
        var order = await Create();
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            await service.GuardarDatosEnvioAsync(manager, order.OrdenId, new(null, "Estandar", 1, null, 2, null,
                app.Clock.GetUtcNow().UtcDateTime.AddHours(2), null, null, Key()));
            var offers = await sp.GetRequiredService<IOrdenService>().GetOrdenesAutoDisponiblesAsync(app.PilotA);
            Assert.DoesNotContain(offers, o => o.OrdenId == order.OrdenId);
        });
        Assert.Equal(409, (await Assert.ThrowsAsync<OperationsException>(() => Assign(order))).StatusCode);
    }

    [Fact]
    public async Task Manager_API_requires_cookie_role_and_CSRF_and_cannot_use_forged_tenant()
    {
        using var pilot = await app.LoginWebAsync("pilot-a");
        Assert.Equal(HttpStatusCode.Forbidden, (await pilot.GetAsync("/api/operaciones/snapshot")).StatusCode);
        using var manager = await app.LoginWebAsync("manager-a");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/operaciones/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/operaciones/zonas", new { nombre = "forged", operacionId = Key() })).StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            await Assert.ThrowsAsync<OperationsException>(() => service.SnapshotAsync(new(app.TenantB, "manager-a")));
            Assert.Equal(0, await sp.GetRequiredService<ApplicationDbContext>().ZonasOperativas.CountAsync());
        });
    }

    [Fact]
    public async Task Editing_operational_notes_preserves_the_existing_schedule_after_dispatch()
    {
        var order = await Create(); var scheduled = app.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var actor = await service.ManagerAsync("manager-a");
            await service.GuardarDatosEnvioAsync(actor, order.OrdenId, new(null, "Estandar", 1, null, 2, null, scheduled, null, "Original", Key()));
        });
        await Assign(order);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var actor = await service.ManagerAsync("manager-a");
            var row = Assert.Single((await service.SnapshotAsync(actor)).DatosEnvios);
            await service.GuardarDatosEnvioAsync(actor, order.OrdenId, new(null, "Estandar", 1, null, 2, null, scheduled, null, "Nota corregida", Key(), row.Version));
            row = Assert.Single((await service.SnapshotAsync(actor)).DatosEnvios);
            Assert.Equal("Nota corregida", row.NotasOperativas); Assert.Equal(scheduled, row.ProgramadaPara);
            await Assert.ThrowsAsync<RejectedOperationException>(() => service.GuardarDatosEnvioAsync(actor, order.OrdenId,
                new(null, "Estandar", 1, null, 2, null, null, null, "No borrar agenda", Key(), row.Version)));
        });
    }

    [Fact]
    public async Task A_committed_key_conflict_is_preserved_while_a_rolled_back_version_conflict_is_explicitly_rejected()
    {
        using var manager = await app.LoginWebAsync("manager-a");
        var html = await manager.GetStringAsync("/Operaciones");
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        manager.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value));
        var create = new GuardarZonaRequest("Zona original", "Centro", "#008b89", [new(18.4, -69.99), new(18.4, -69.9), new(18.5, -69.9)], 10,
            "ParadasPendientes", 80, "07:00", "22:00", true, Key());
        var response = await manager.PostAsJsonAsync("/api/operaciones/zonas", create); response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var conflict = await manager.PostAsJsonAsync("/api/operaciones/zonas", create with { Nombre = "Otro contenido" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.False((await conflict.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("solicitudRechazada", out _));
        var edit = create with { Id = created.GetProperty("id").GetInt32(), Version = created.GetProperty("version").GetString(), OperacionId = Key() };
        var accepted = await manager.PostAsJsonAsync("/api/operaciones/zonas", edit); accepted.EnsureSuccessStatusCode();
        var rejected = await manager.PostAsJsonAsync("/api/operaciones/zonas", edit with { OperacionId = Key() });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.True((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("solicitudRechazada").GetBoolean());
    }

    [Fact]
    public async Task Scheduled_incident_scan_is_tenant_scoped_idempotent_and_records_system_authorship()
    {
        var order = await Create();
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var actor = await service.ManagerAsync("manager-a");
            await service.GuardarReglaIncidenciaAsync(actor, new("Demora", 3, 15, "Continuar", true, true, Key()));
            await service.GuardarDatosEnvioAsync(actor, order.OrdenId, new(null, "Estandar", 1, null, 2, null, null,
                app.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10), null, Key()));
        });
        await app.InScopeAsync(async sp => await ActivatorUtilities.CreateInstance<OperationsAutomationWorker>(sp).ScanAsync(CancellationToken.None));
        await app.InScopeAsync(async sp => await ActivatorUtilities.CreateInstance<OperationsAutomationWorker>(sp).ScanAsync(CancellationToken.None));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>(); var incident = await db.IncidenciasOperativas.Include(i => i.Actividad).SingleAsync();
            Assert.Equal(app.TenantA, incident.ClienteAppId); Assert.Equal("System", incident.ReportadaPor);
            Assert.Equal("System", Assert.Single(incident.Actividad).ActorId); Assert.Equal(order.OrdenId, incident.OrdenEntregaId);
        });
    }

    [Fact]
    public async Task Zone_geometry_and_tenant_relations_are_validated_before_persisting()
    {
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            var valid = new GuardarZonaRequest("Centro QA", "Santo Domingo", "#008b89", [new(18.40, -69.99), new(18.40, -69.90), new(18.50, -69.90), new(18.50, -69.99)],
                50, "ParadasPendientes", 80, "07:00", "22:00", true, Key());
            var ack = Ack(await service.GuardarZonaAsync(manager, valid)); Assert.True(ack.GetProperty("id").GetInt32() > 0);
            await Assert.ThrowsAsync<RejectedOperationException>(() => service.GuardarZonaAsync(manager, valid with { Nombre = "Cruce", OperacionId = Key(),
                Poligono = [new(18.40, -69.99), new(18.50, -69.90), new(18.40, -69.90), new(18.50, -69.99)] }));
            Assert.True(Assert.Single((await service.SnapshotAsync(manager)).Zonas).SuperficieKm2 > 0);
        });
    }
}
