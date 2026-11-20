using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ReasignacionFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private OperationsActor Manager => new(app.TenantA, "manager-a");
    private OperationsActor First => new(app.TenantA, "pilot-a", app.PilotA);
    private OperationsActor Second => new(app.TenantA, "pilot-a2", app.PilotA2);
    private static string Key() => Guid.NewGuid().ToString("N");
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private async Task<JsonElement> Command(Func<EnvirapidoOperationsService, Task<object>> action)
    {
        JsonElement value = default;
        await app.InScopeAsync(async sp => value = JsonSerializer.SerializeToElement(await action(sp.GetRequiredService<EnvirapidoOperationsService>()), Json));
        return value;
    }
    private async Task<OrdenCreadaResult> Order(bool collected = true, int count = 3)
    {
        OrdenCreadaResult result = null!;
        await app.InScopeAsync(async sp => {
            var core = sp.GetRequiredService<IOrdenService>();
            result = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key(),
                destinatarios: Enumerable.Range(0, count).Select(i => new OrdenDestinatarioRequest(app.CustomerA, $"Paquete {i + 1}")).ToArray());
            await core.AsignarPilotoAsync(result.CodigoB, app.PilotA, "manager-a", "Gestor", result.OrdenId);
            if (collected) await core.ConfirmarRecolectaAsync(result.CodigoB, "pilot-a", "Piloto", result.OrdenId, Key());
        });
        return result;
    }
    private async Task<List<RevisionParadaRequest>> Selection(int orderId, params int[] stopIds)
    {
        List<RevisionParadaRequest> result = [];
        await app.InScopeAsync(async sp => result = await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega
            .Where(p => p.OrdenEntregaId == orderId && stopIds.Contains(p.Id))
            .Select(p => new RevisionParadaRequest(p.Id, p.RevisionAsignacion)).ToListAsync());
        return result;
    }
    private async Task<RetiroParadaDto> Retire(OrdenCreadaResult order, int index = 0, bool pilot = false)
    {
        var selection = await Selection(order.OrdenId, order.Paradas[index].ParadaId);
        var result = await Command(s => s.SolicitarRetiroParadasAsync(pilot ? First : Manager,
            new(order.OrdenId, selection, "Incidencia de prueba", Key())));
        return result.GetProperty("gestiones")[0].Deserialize<RetiroParadaDto>(Json)!;
    }
    private async Task<RetiroParadaDto> Assign(OrdenCreadaResult order, int index = 0)
    {
        var selection = await Selection(order.OrdenId, order.Paradas[index].ParadaId);
        var result = await Command(s => s.AsignarParadasAsync(Manager, new(order.OrdenId, selection, app.PilotA2, "Cambio de piloto autorizado", Key())));
        return result.GetProperty("gestiones")[0].Deserialize<RetiroParadaDto>(Json)!;
    }

    [Fact]
    public async Task PrePickupSplitKeepsSiblingsCodesAndSingleOrder()
    {
        var order = await Order(false);
        var result = await Assign(order);
        Assert.Equal("Reasignada", result.Estado);
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var parent = await db.OrdenesEntrega.Include(o => o.Paradas).SingleAsync(o => o.Id == order.OrdenId);
            Assert.True(parent.GestionPorParadas); Assert.False(parent.EsAuto); Assert.Null(parent.PilotoId);
            Assert.Equal(app.PilotA2, parent.Paradas.Single(p => p.Id == order.Paradas[0].ParadaId).PilotoAsignadoId);
            Assert.All(parent.Paradas.Where(p => p.Id != order.Paradas[0].ParadaId), p => Assert.Equal(app.PilotA, p.PilotoAsignadoId));
            Assert.Equal(1, await db.OrdenesEntrega.CountAsync());
            Assert.All(await db.CodigosEntrega.ToListAsync(), c => Assert.True(c.Reservado));
        });
    }

    [Fact]
    public async Task PostPickupTransferRequiresActualReceiverAndIdempotentReceipt()
    {
        var order = await Order();
        await Retire(order);
        var transfer = await Assign(order);
        Assert.Equal("PendienteRecepcion", transfer.Estado); Assert.Equal(app.PilotA, transfer.PilotoCustodioId);
        var receipt = new ConfirmarRecepcionParadaRequest(transfer.Version, "Recibí físicamente el paquete", Key());
        await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.ConfirmarRecepcionParadaAsync(First, transfer.Id, receipt)));
        var first = await Command(s => s.ConfirmarRecepcionParadaAsync(Second, transfer.Id, receipt));
        var replay = await Command(s => s.ConfirmarRecepcionParadaAsync(Second, transfer.Id, receipt));
        Assert.Equal(first.GetRawText(), replay.GetRawText());
        Assert.Equal("Recibida", first.GetProperty("estado").GetString());
        await app.InScopeAsync(async sp => {
            var p = await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega.SingleAsync(p => p.Id == transfer.ParadaId);
            Assert.Equal(app.PilotA2, p.PilotoCustodioId); Assert.False(p.EnReasignacion);
            Assert.NotNull(p.RecogidaParadaAt); Assert.Null(p.EntregadaAt);
        });
    }

    [Fact]
    public async Task AuthorizedReturnToCenterKeepsCReservedAndReturnsToQueue()
    {
        var order = await Order(); var retired = await Retire(order);
        var planned = (await Command(s => s.ProgramarRetornoParadaAsync(Manager, retired.Id,
            new(retired.Version, Key(), NombreCentro: "Centro A", DireccionCentro: "Almacén de la empresa")))).Deserialize<RetiroParadaDto>(Json)!;
        await Command(s => s.ConfirmarRecepcionParadaAsync(Manager, planned.Id, new(planned.Version, "Recibido en almacén", Key())));
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var p = await db.ParadasEntrega.SingleAsync(p => p.Id == planned.ParadaId);
            Assert.True(p.EnReasignacion); Assert.Null(p.PilotoCustodioId); Assert.Null(p.PilotoAsignadoId);
            Assert.Equal("Almacén de la empresa", p.RecogidaActualDireccion);
            Assert.True(await db.CodigosEntrega.Where(c => c.ParadaEntregaId == p.Id).Select(c => c.Reservado).SingleAsync());
            Assert.Equal(Login.Domain.Entities.EstadoParadaEntrega.Pendiente, p.Estado);
        });
        Assert.Equal("PendienteRecepcion", (await Assign(order)).Estado);
    }

    [Fact]
    public async Task PilotRequestNeedsManagerDecisionAndCannotMoveOtherStops()
    {
        var order = await Order(); var request = await Retire(order, pilot: true);
        Assert.Equal("Solicitada", request.Estado);
        await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.DecidirRetiroParadaAsync(First, request.Id,
            new("Aprobar", "Intento no autorizado", request.Version, Key()))));
        await Command(s => s.DecidirRetiroParadaAsync(Manager, request.Id, new("Aprobar", "Incidencia confirmada", request.Version, Key())));
        await app.InScopeAsync(async sp => {
            var stops = await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega.Where(p => p.OrdenEntregaId == order.OrdenId).ToListAsync();
            Assert.True(stops.Single(p => p.Id == request.ParadaId).EnReasignacion);
            Assert.All(stops.Where(p => p.Id != request.ParadaId), p => { Assert.Equal(app.PilotA, p.PilotoAsignadoId); Assert.False(p.EnReasignacion); });
        });
    }

    [Fact]
    public async Task CrossTenantTargetAndStaleAssignmentAreRejectedWithoutMovingPackage()
    {
        var order = await Order(false);
        var original = await Selection(order.OrdenId, order.Paradas[0].ParadaId);
        await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, original, app.PilotB, "Destino de otra empresa", Key()))));
        await Assign(order);
        await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, original, app.PilotA, "Intento antiguo", Key()))));
        await app.InScopeAsync(async sp => Assert.Equal(app.PilotA2, (await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega
            .SingleAsync(p => p.Id == original[0].ParadaId)).PilotoAsignadoId));
    }

    [Fact]
    public async Task CancelCollectedStopClosesDeliveryButKeepsReturnCustodyUntilReceipt()
    {
        var order = await Order(count: 1); var retired = await Retire(order);
        var selected = await Selection(order.OrdenId, retired.ParadaId);
        await Command(s => s.CancelarParadasAsync(Manager, new(order.OrdenId, selected, "Cliente canceló entrega", Key())));
        RetiroParadaDto pending = null!;
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Cancelada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            Assert.Equal(app.PilotA, (await db.ParadasEntrega.SingleAsync()).PilotoCustodioId);
            pending = (await sp.GetRequiredService<EnvirapidoOperationsService>().ReasignacionesAsync(Manager)).Gestiones.Single(g => g.Abierta);
        });
        var planned = (await Command(s => s.ProgramarRetornoParadaAsync(Manager, pending.Id,
            new(pending.Version, Key(), app.ShopA2)))).Deserialize<RetiroParadaDto>(Json)!;
        await Command(s => s.ConfirmarRecepcionParadaAsync(Manager, planned.Id, new(planned.Version, "Recibido tras cancelación", Key())));
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Null((await db.ParadasEntrega.SingleAsync()).PilotoCustodioId);
            Assert.Equal(EstadoOrden.Cancelada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            Assert.Null((await db.OrdenesEntrega.SingleAsync()).EntregadaAt);
        });
    }

    [Fact]
    public async Task StoreReceiverOnlySeesItsDestinationAndCannotApproveRetirements()
    {
        var order = await Order(); var retired = await Retire(order);
        var planned = (await Command(s => s.ProgramarRetornoParadaAsync(Manager, retired.Id,
            new(retired.Version, Key(), app.ShopA)))).Deserialize<RetiroParadaDto>(Json)!;
        OperationsActor store = null!;
        await app.InScopeAsync(async sp => {
            var s = sp.GetRequiredService<EnvirapidoOperationsService>();
            store = await s.ReceptorComercioAsync("shop-a", app.ShopA);
            var visible = await s.ReasignacionesAsync(store);
            Assert.Empty(visible.Paradas); Assert.Single(visible.Gestiones);
            await Assert.ThrowsAsync<OperationsException>(() => s.ReceptorComercioAsync("shop-b", app.ShopA));
        });
        await Command(s => s.ConfirmarRecepcionParadaAsync(store, planned.Id, new(planned.Version, "Comercio recibió el paquete", Key())));
        await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.AsignarParadasAsync(store,
            new(order.OrdenId, [], app.PilotA2, "Sin permiso gestor", Key()))));
    }
}
