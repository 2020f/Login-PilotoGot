using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class OperationsRegressionTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Cancelling_active_route_after_a_child_order_never_reopens_the_cancelled_order()
    {
        var cancelledId = 0; var otherId = 0;
        await app.InScopeAsync(async sp => {
            var orders = sp.GetRequiredService<IOrdenService>();
            var first = await orders.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
            var second = await orders.CrearOrdenAsync(app.TenantA, app.ShopA2, app.CustomerA2, operacionId: Key());
            cancelledId = first.OrdenId; otherId = second.OrdenId;
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var manager = await service.ManagerAsync("manager-a");
            var route = Ack(await service.CrearRutaAsync(manager, new("Cancelación parcial QA", DateOnly.FromDateTime(DateTime.UtcNow),
                [first.OrdenId, second.OrdenId], app.PilotA, null, false, Key())));
            var routeId = route.GetProperty("id").GetInt32();
            await service.EstadoRutaAsync(manager, routeId, new("Iniciar", route.GetProperty("version").GetString()!, Key()));
            await service.CancelarOrdenAsync(manager, first.OrdenId, new("Comercio canceló antes de recoger", Key()));
            var snapshot = await service.SnapshotAsync(manager);
            var current = Assert.Single(snapshot.Rutas);
            await service.EstadoRutaAsync(manager, routeId, new("Cancelar", current.Version, Key()));
        });
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var cancelled = await db.OrdenesEntrega.Include(o => o.Paradas).Include(o => o.Codigos).SingleAsync(o => o.Id == cancelledId);
            Assert.Equal(EstadoOrden.Cancelada, cancelled.Estado); Assert.NotNull(cancelled.FinalizadaAt); Assert.Null(cancelled.RecolectadaAt);
            Assert.All(cancelled.Paradas, p => Assert.Equal(EstadoParadaEntrega.Cancelada, p.Estado));
            Assert.All(cancelled.Codigos, c => { Assert.False(c.Reservado); Assert.False(c.Usado); });
            Assert.NotNull((await db.DatosEnviosOperativos.SingleAsync(d => d.OrdenEntregaId == cancelledId)).CanceladaAt);
            var remaining = await db.OrdenesEntrega.SingleAsync(o => o.Id == otherId);
            Assert.Equal(EstadoOrden.Creada, remaining.Estado); Assert.Null(remaining.PilotoId);
            Assert.Equal(EstadoRutaOperativa.Cancelada, (await db.RutasOperativas.SingleAsync()).Estado);
            Assert.False(await db.RutaOrdenesOperativas.AnyAsync(x => x.Vigente));
            Assert.DoesNotContain(await sp.GetRequiredService<IOrdenService>().GetOrdenesAutoDisponiblesAsync(app.PilotA), o => o.OrdenId == cancelledId);
        });
    }

    [Fact]
    public async Task Pilot_snapshot_hides_CRM_notes_activity_and_resolution_until_each_order_is_picked_up()
    {
        var created = new List<OrdenCreadaResult>();
        await app.InScopeAsync(async sp => {
            var core = sp.GetRequiredService<IOrdenService>();
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var manager = await service.ManagerAsync("manager-a");
            for (var i = 0; i < 2; i++)
            {
                var order = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
                await core.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
                await service.GuardarDatosEnvioAsync(manager, order.OrdenId, new(null, "Estandar", 1, null, 2, null,
                    null, null, $"CRM_PRIVATE_NOTE_{i} telefono destino 8091234567", Key()));
                var incident = Ack(await service.ReportarIncidenciaAsync(manager, new("Direccion incompleta",
                    $"CRM_PRIVATE_DESCRIPTION_{i} direccion del cliente", Key(), order.OrdenId, order.Paradas[0].ParadaId)));
                var active = Ack(await service.CambiarIncidenciaAsync(manager, incident.GetProperty("id").GetInt32(),
                    new("Atender", $"CRM_PRIVATE_ACTIVITY_{i} contacto destinatario", incident.GetProperty("version").GetString()!, Key())));
                await service.CambiarIncidenciaAsync(manager, incident.GetProperty("id").GetInt32(),
                    new("Resolver", $"CRM_PRIVATE_RESOLUTION_{i} ubicacion confirmada", active.GetProperty("version").GetString()!, Key()));
                created.Add(order);
            }
            var authorized = JsonSerializer.Serialize(await service.SnapshotAsync(manager));
            Assert.Contains("CRM_PRIVATE_NOTE_0", authorized); Assert.Contains("CRM_PRIVATE_DESCRIPTION_1", authorized);
        });
        using var pilot = await app.LoginAsync("pilot-a");
        var beforeResponse = await pilot.GetAsync("/api/piloto/operaciones"); beforeResponse.EnsureSuccessStatusCode();
        var before = await beforeResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("CRM_PRIVATE_", before);
        var beforeDto = JsonSerializer.Deserialize<OperationsSnapshot>(before, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.All(beforeDto.DatosEnvios, d => Assert.Null(d.NotasOperativas));
        Assert.All(beforeDto.Incidencias, i => { Assert.Empty(i.Actividad); Assert.Null(i.Resolucion); });
        Assert.Empty(beforeDto.Eventos!);
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { created[0].OrdenId,
            created[0].CodigoB, operacionId = Key() })).EnsureSuccessStatusCode();
        var after = await pilot.GetStringAsync("/api/piloto/operaciones");
        foreach (var kind in new[] { "NOTE", "DESCRIPTION", "ACTIVITY", "RESOLUTION" })
        {
            Assert.Contains($"CRM_PRIVATE_{kind}_0", after);
            Assert.DoesNotContain($"CRM_PRIVATE_{kind}_1", after);
        }
    }
}
