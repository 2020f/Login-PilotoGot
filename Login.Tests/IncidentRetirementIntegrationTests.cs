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

public sealed class IncidentRetirementIntegrationTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private OperationsActor Manager => new(app.TenantA, "manager-a");
    private OperationsActor Pilot => new(app.TenantA, "pilot-a", app.PilotA);
    private OperationsActor Receiver => new(app.TenantA, "pilot-a2", app.PilotA2);
    private static string Key() => Guid.NewGuid().ToString("N");
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    private async Task<JsonElement> Command(Func<EnvirapidoOperationsService, Task<object>> action)
    {
        JsonElement result = default;
        await app.InScopeAsync(async sp => result = JsonSerializer.SerializeToElement(
            await action(sp.GetRequiredService<EnvirapidoOperationsService>()), Json));
        return result;
    }
    private async Task<OrdenCreadaResult> Order(bool picked = false, int count = 2)
    {
        OrdenCreadaResult order = null!;
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrdenService>();
            order = await service.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key(),
                destinatarios: Enumerable.Range(0, count).Select(_ => new OrdenDestinatarioRequest(app.CustomerA)).ToArray());
            await service.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            if (picked) await service.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key());
        });
        return order;
    }
    private async Task<List<RevisionParadaRequest>> Selection(OrdenCreadaResult order, params int[] indices)
    {
        var ids = indices.Select(i => order.Paradas[i].ParadaId).ToArray();
        List<RevisionParadaRequest> result = [];
        await app.InScopeAsync(async sp => result = await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega
            .Where(p => p.ClienteAppId == app.TenantA && p.OrdenEntregaId == order.OrdenId && ids.Contains(p.Id))
            .Select(p => new RevisionParadaRequest(p.Id, p.RevisionAsignacion)).ToListAsync());
        return result;
    }
    private async Task<int> Incident(OrdenCreadaResult order, int? stop = null)
        => (await Command(s => s.ReportarIncidenciaAsync(Pilot,
            new("General", "Paquete requiere gestión del responsable", Key(), order.OrdenId,
                stop.HasValue ? order.Paradas[stop.Value].ParadaId : null)))).GetProperty("id").GetInt32();
    private async Task<DevolucionOperativaDto> Return(OrdenCreadaResult order, int index, string state)
    {
        var dto = (await Command(s => s.SolicitarDevolucionAsync(Pilot,
            new(order.OrdenId, order.Paradas[index].ParadaId, "Devolucion", "Sin contacto", Key()))))
            .Deserialize<DevolucionOperativaDto>(Json)!;
        if (state != "Solicitada") dto = (await Command(s => s.DecidirDevolucionAsync(Manager, dto.Id,
            new("Autorizar", "Retorno revisado por gestor", dto.Version, Key())))).Deserialize<DevolucionOperativaDto>(Json)!;
        if (state == "EnRetorno") dto = (await Command(s => s.IniciarRetornoAsync(Pilot, dto.Id,
            new(dto.Version, Key())))).Deserialize<DevolucionOperativaDto>(Json)!;
        return dto;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pilot_incident_request_approval_transfer_and_receipt_keep_codes_and_custody(bool picked)
    {
        var order = await Order(picked);
        var request = new SolicitarRetiroParadasRequest(order.OrdenId, await Selection(order, 0),
            "Solicito devolución y reasignación del paquete", Key(), CrearIncidencia: true);
        var first = await Command(s => s.SolicitarRetiroParadasAsync(Pilot, request));
        Assert.Equal(first.GetRawText(), (await Command(s => s.SolicitarRetiroParadasAsync(Pilot, request))).GetRawText());
        var item = first.GetProperty("gestiones")[0].Deserialize<RetiroParadaDto>(Json)!;
        Assert.Equal("Solicitada", item.Estado); Assert.NotNull(item.IncidenciaId);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.IncidenciasOperativas.ToListAsync());
            Assert.Equal(app.PilotA, (await db.ParadasEntrega.SingleAsync(p => p.Id == item.ParadaId)).PilotoAsignadoId);
        });
        var approved = (await Command(s => s.DecidirRetiroParadaAsync(Manager, item.Id,
            new("Aprobar", "Se retira sin confirmar entrega", item.Version, Key())))).Deserialize<RetiroParadaDto>(Json)!;
        Assert.Equal("PorReasignar", approved.Estado);
        Assert.Equal(picked ? app.PilotA : null, approved.PilotoCustodioId);
        var approvedSelection = await Selection(order, 0);
        var transfer = (await Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, approvedSelection, app.PilotA2, "Cambio autorizado", Key(), item.IncidenciaId))))
            .GetProperty("gestiones")[0].Deserialize<RetiroParadaDto>(Json)!;
        Assert.Equal(item.IncidenciaId, transfer.IncidenciaId);
        Assert.Equal(picked ? "PendienteRecepcion" : "Reasignada", transfer.Estado);
        if (picked)
        {
            await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.ConfirmarRecepcionParadaAsync(Pilot,
                transfer.Id, new(transfer.Version, "No es el receptor", Key()))));
            await Command(s => s.ConfirmarRecepcionParadaAsync(Receiver, transfer.Id,
                new(transfer.Version, "Recibido físicamente", Key())));
        }
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var stops = await db.ParadasEntrega.Where(p => p.OrdenEntregaId == order.OrdenId).OrderBy(p => p.Posicion).ToListAsync();
            Assert.Equal(app.PilotA2, stops[0].PilotoAsignadoId); Assert.Equal(app.PilotA, stops[1].PilotoAsignadoId);
            Assert.Null(stops[0].EntregadaAt); Assert.Equal(EstadoParadaEntrega.Pendiente, stops[0].Estado);
            Assert.All(await db.CodigosEntrega.Where(c => c.Tipo == TipoCodigo.C_Finalizacion).ToListAsync(), c => { Assert.False(c.Usado); Assert.True(c.Reservado); });
            var incident = await db.IncidenciasOperativas.Include(i => i.Actividad).SingleAsync();
            Assert.Equal(EstadoIncidencia.EnAtencion, incident.Estado);
            Assert.Contains(incident.Actividad, x => x.Accion == "RetiroAprobado");
            Assert.Contains(incident.Actividad, x => x.Accion == "Reasignacion");
            Assert.Equal(picked, incident.Actividad.Any(x => x.Accion == "RecepcionPaquete"));
        });
    }

    [Fact]
    public async Task Existing_incident_can_retire_without_new_incident_or_pickup_codes_and_scope_is_strict()
    {
        var order = await Order(); var incident = await Incident(order, 0);
        var padded = new SolicitarRetiroParadasRequest(order.OrdenId, await Selection(order, 0, 1), "Afectaría paradas extra", Key(), incident);
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.SolicitarRetiroParadasAsync(Manager, padded)))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, padded.Paradas, app.PilotA2, "Alcance excesivo", Key(), incident))))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.CancelarParadasAsync(Manager,
            new(order.OrdenId, padded.Paradas, "Alcance excesivo", Key(), incident))))).StatusCode);
        var selected = await Selection(order, 0);
        var ack = await Command(s => s.SolicitarRetiroParadasAsync(Manager,
            new(order.OrdenId, selected, "Retiro por incidencia", Key(), incident)));
        Assert.Equal("PorReasignar", ack.GetProperty("gestiones")[0].GetProperty("estado").GetString());
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.IncidenciasOperativas.ToListAsync());
            Assert.Null((await db.ParadasEntrega.SingleAsync(p => p.Id == selected[0].ParadaId)).PilotoAsignadoId);
            Assert.Equal(app.PilotA, (await db.ParadasEntrega.SingleAsync(p => p.Id == order.Paradas[1].ParadaId)).PilotoAsignadoId);
        });
    }

    [Fact]
    public async Task Foreign_incident_and_target_and_ambiguous_creation_are_rejected_atomically()
    {
        var order = await Order(); var selected = await Selection(order, 0);
        var incident = await Incident(order, 0);
        var other = 0;
        await app.InScopeAsync(async sp =>
        {
            var otherOrder = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantB, app.ShopB, app.CustomerB, operacionId: Key());
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var foreign = new IncidenciaOperativa { ClienteAppId = app.TenantB, OrdenEntregaId = otherOrder.OrdenId, Descripcion = "Otro tenant", ReportadaPor = "pilot-b" };
            db.IncidenciasOperativas.Add(foreign); await db.SaveChangesAsync(); other = foreign.Id;
        });
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.SolicitarRetiroParadasAsync(Manager,
            new(order.OrdenId, selected, "Caso ajeno", Key(), other))))).StatusCode);
        await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.SolicitarRetiroParadasAsync(Pilot,
            new(order.OrdenId, selected, "Ambiguo", Key(), incident, CrearIncidencia: true))));
        await Assert.ThrowsAnyAsync<OperationsException>(() => Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, selected, app.PilotB, "Destino ajeno", Key(), incident))));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.RetirosParadas.ToListAsync());
            Assert.False((await db.OrdenesEntrega.SingleAsync(o => o.Id == order.OrdenId)).GestionPorParadas);
            Assert.Equal(2, await db.IncidenciasOperativas.CountAsync());
        });
    }

    [Theory]
    [InlineData("Solicitada")]
    [InlineData("Autorizada")]
    [InlineData("EnRetorno")]
    public async Task Explicit_legacy_handoff_moves_all_open_returns_keeps_custody_and_replays(string state)
    {
        var order = await Order(true, 3); var incident = await Incident(order, 0);
        var first = await Return(order, 0, state); var second = await Return(order, 1, "Solicitada");
        var request = new TrasladarDevolucionesRequest(order.OrdenId,
            [new(first.Id, first.Version), new(second.Id, second.Version)], "Gestor mueve las dos devoluciones a reasignación", Key(), incident);
        await Assert.ThrowsAnyAsync<OperationsException>(() => Command(s => s.TrasladarDevolucionesAReasignacionAsync(Pilot, request)));
        var ack = await Command(s => s.TrasladarDevolucionesAReasignacionAsync(Manager, request));
        Assert.Equal(ack.GetRawText(), (await Command(s => s.TrasladarDevolucionesAReasignacionAsync(Manager, request))).GetRawText());
        Assert.Equal(2, ack.GetProperty("gestiones").GetArrayLength());
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.All(await db.DevolucionesOperativas.ToListAsync(), x => { Assert.False(x.Abierta); Assert.Equal(EstadoDevolucionOperativa.TrasladadaAReasignacion, x.Estado); Assert.Null(x.RecibidaAt); });
            var retired = await db.ParadasEntrega.Where(p => p.OrdenEntregaId == order.OrdenId && p.Id != order.Paradas[2].ParadaId).ToListAsync();
            Assert.All(retired, p => { Assert.True(p.EnReasignacion); Assert.Null(p.PilotoAsignadoId); Assert.Equal(app.PilotA, p.PilotoCustodioId); Assert.NotNull(p.RecogidaParadaAt); Assert.Equal(EstadoParadaEntrega.Pendiente, p.Estado); });
            Assert.Equal(app.PilotA, (await db.ParadasEntrega.SingleAsync(p => p.Id == order.Paradas[2].ParadaId)).PilotoAsignadoId);
            var retiros = await db.RetirosParadas.OrderBy(x => x.ParadaEntregaId).ToListAsync();
            Assert.Equal(incident, retiros.Single(x => x.ParadaEntregaId == first.ParadaId).IncidenciaId);
            Assert.Null(retiros.Single(x => x.ParadaEntregaId == second.ParadaId).IncidenciaId);
            Assert.All(await db.CodigosEntrega.Where(c => c.Tipo == TipoCodigo.C_Finalizacion).ToListAsync(), c => { Assert.False(c.Usado); Assert.True(c.Reservado); });
        });
        var selection = await Selection(order, 0);
        var transfer = await Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, selection, app.PilotA2, "Ahora se permite el traspaso", Key(), incident)));
        Assert.Equal("PendienteRecepcion", transfer.GetProperty("gestiones")[0].GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Legacy_handoff_requires_current_complete_versions_and_normal_commands_do_not_silently_migrate()
    {
        var order = await Order(true); var first = await Return(order, 0, "Solicitada"); var second = await Return(order, 1, "Autorizada");
        var selected = await Selection(order, 0);
        await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.SolicitarRetiroParadasAsync(Manager,
            new(order.OrdenId, selected, "No convertir automáticamente", Key()))));
        await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.TrasladarDevolucionesAReasignacionAsync(Manager,
            new(order.OrdenId, [new(first.Id, first.Version)], "Falta otra devolución", Key()))));
        await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.TrasladarDevolucionesAReasignacionAsync(Manager,
            new(order.OrdenId, [new(first.Id, first.Version), new(second.Id, Key())], "Versión incorrecta", Key()))));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.All(await db.DevolucionesOperativas.ToListAsync(), d => Assert.True(d.Abierta));
            Assert.Empty(await db.RetirosParadas.ToListAsync());
            Assert.False((await db.OrdenesEntrega.SingleAsync()).GestionPorParadas);
        });
    }

    [Fact]
    public async Task Legacy_handoff_releases_only_affected_route_members_and_keeps_sibling_execution()
    {
        var order = await Order(false, 3);
        var route = await Command(s => s.CrearRutaAsync(Manager,
            new("Ruta con retorno", DateOnly.FromDateTime(app.Clock.GetUtcNow().UtcDateTime), [order.OrdenId], app.PilotA, null, false, Key())));
        await Command(s => s.EstadoRutaAsync(Manager, route.GetProperty("id").GetInt32(),
            new("Iniciar", route.GetProperty("version").GetString()!, Key())));
        await app.InScopeAsync(async sp => await sp.GetRequiredService<IOrdenService>()
            .ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key()));
        var first = await Return(order, 0, "Autorizada");
        await Command(s => s.TrasladarDevolucionesAReasignacionAsync(Manager,
            new(order.OrdenId, [new(first.Id, first.Version)], "Retiro de retorno sin afectar las otras entregas", Key())));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var memberships = await db.RutaOrdenesOperativas.Where(m => m.OrdenEntregaId == order.OrdenId).ToListAsync();
            Assert.DoesNotContain(memberships, m => m.Vigente && m.ParadaEntregaId == first.ParadaId);
            Assert.Equal(2, memberships.Count(m => m.Vigente));
            Assert.All(await db.PuntosRutaOperativos.Where(p => p.Tipo == TipoPuntoRuta.Devolucion).Include(p => p.RutaOrden).ToListAsync(), p => Assert.False(p.RutaOrden.Vigente));
            Assert.Equal(EstadoRutaOperativa.Activa, (await db.RutasOperativas.SingleAsync()).Estado);
        });
    }

    [Fact]
    public async Task A_stop_pause_requires_separate_requests_and_does_not_partially_convert_the_order()
    {
        var order = await Order(false, 3);
        await Command(s => s.GuardarReglaIncidenciaAsync(Manager,
            new("Retiro de paquete", 2, 30, "PausarParada", true, false, Key())));
        var selection = await Selection(order, 0, 1);
        var error = await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.SolicitarRetiroParadasAsync(Pilot,
            new(order.OrdenId, selection, "Retiro de dos paradas sin pausar la tercera", Key(), CrearIncidencia: true))));
        Assert.Equal(409, error.StatusCode); Assert.Contains("por separado para cada parada", error.Message);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.IncidenciasOperativas.ToListAsync());
            Assert.Empty(await db.RetirosParadas.ToListAsync());
            var persisted = await db.OrdenesEntrega.Include(o => o.Paradas).SingleAsync(o => o.Id == order.OrdenId);
            Assert.False(persisted.GestionPorParadas); Assert.Equal(app.PilotA, persisted.PilotoId);
            Assert.All(persisted.Paradas, p => { Assert.Null(p.PilotoAsignadoId); Assert.False(p.EnReasignacion); });
        });
    }

    [Fact]
    public async Task A_retirement_route_pause_remains_on_sibling_orders_after_approving_the_withdrawal()
    {
        var withdrawn = await Order(false, 1); var sibling = await Order(false, 1);
        var route = await Command(s => s.CrearRutaAsync(Manager,
            new("Ruta compartida con pausa", DateOnly.FromDateTime(app.Clock.GetUtcNow().UtcDateTime),
                [withdrawn.OrdenId, sibling.OrdenId], app.PilotA, null, false, Key())));
        var routeId = route.GetProperty("id").GetInt32();
        await Command(s => s.EstadoRutaAsync(Manager, routeId,
            new("Iniciar", route.GetProperty("version").GetString()!, Key())));
        await Command(s => s.GuardarReglaIncidenciaAsync(Manager,
            new("Retiro de paquete", 2, 30, "PausarRuta", true, false, Key())));
        var selection = await Selection(withdrawn, 0);
        var requested = (await Command(s => s.SolicitarRetiroParadasAsync(Pilot,
            new(withdrawn.OrdenId, selection, "Retiro sin levantar la pausa de ruta", Key(), CrearIncidencia: true))))
            .GetProperty("gestiones")[0].Deserialize<RetiroParadaDto>(Json)!;
        await Command(s => s.DecidirRetiroParadaAsync(Manager, requested.Id,
            new("Aprobar", "Retiro autorizado; la incidencia sigue abierta", requested.Version, Key())));
        string incidentVersion = "";
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var incident = await db.IncidenciasOperativas.SingleAsync(i => i.Id == requested.IncidenciaId);
            Assert.Equal(routeId, incident.RutaId); Assert.NotEqual(EstadoIncidencia.Resuelta, incident.Estado);
            incidentVersion = incident.Version;
            Assert.DoesNotContain(await db.RutaOrdenesOperativas.Where(m => m.OrdenEntregaId == withdrawn.OrdenId).ToListAsync(), m => m.Vigente);
            Assert.True(await db.RutaOrdenesOperativas.AnyAsync(m => m.OrdenEntregaId == sibling.OrdenId && m.Vigente));
            var error = await Assert.ThrowsAsync<OperationsException>(() => new OperationsWorkGuard(db, app.Clock)
                .ValidateAsync(app.TenantA, sibling.OrdenId, null, true));
            Assert.Equal(409, error.StatusCode); Assert.Contains("política de una incidencia", error.Message);
        });
        await Command(s => s.CambiarIncidenciaAsync(Manager, requested.IncidenciaId!.Value,
            new("Resolver", "El gestor libera expresamente la pausa de ruta", incidentVersion, Key())));
        // La recogida clásica corresponde a una visita del pedido completo,
        // con ParadaEntregaId null. Ejecutar el comando real conserva ese control
        // de secuencia en lugar de validar como si ya fuera una recogida por parada.
        await app.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IOrdenService>().ConfirmarRecolectaAsync(sibling.CodigoB,
                "pilot-a", "Piloto", sibling.OrdenId, Key());
            Assert.Equal(EstadoOrden.Recolectada, (await sp.GetRequiredService<ApplicationDbContext>()
                .OrdenesEntrega.SingleAsync(o => o.Id == sibling.OrdenId)).Estado);
        });
    }

    [Fact]
    public async Task Active_assignment_view_is_manager_only_and_pending_legacy_cases_do_not_leak_to_pilots()
    {
        var order = await Order(true); await Return(order, 0, "Solicitada");
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            Assert.Empty((await service.ReasignacionesAsync(Manager)).Paradas);
            var view = await service.ReasignacionesAsync(Manager, incluirActivas: true);
            Assert.Equal(2, view.Paradas.Count); Assert.Single(view.DevolucionesPendientes!);
            var pilot = await service.ReasignacionesAsync(Pilot, incluirActivas: true);
            Assert.Empty(pilot.Paradas); Assert.Empty(pilot.DevolucionesPendientes!);
            var foreign = await service.ReasignacionesAsync(new(app.TenantB, "pilot-b", app.PilotB), incluirActivas: true);
            Assert.Empty(foreign.Paradas); Assert.Empty(foreign.Gestiones); Assert.Empty(foreign.DevolucionesPendientes!);
        });
    }

    [Fact]
    public async Task Concurrent_pilot_requests_cannot_create_an_orphan_incident_or_duplicate_retirement()
    {
        var order = await Order(); var selection = await Selection(order, 0);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await Command(s => s.SolicitarRetiroParadasAsync(Pilot,
                    new(order.OrdenId, selection, "Solicitudes simultáneas", Key(), CrearIncidencia: true)));
                return 200;
            }
            catch (OperationsException e) { return e.StatusCode; }
            catch (RejectedOperationException e) { return e.StatusCode; }
        }));
        Assert.Equal([200, 409], results.Order().ToArray());
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.IncidenciasOperativas.ToListAsync());
            Assert.Single(await db.RetirosParadas.ToListAsync());
        });
    }
}
