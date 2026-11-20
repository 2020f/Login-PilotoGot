using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class AjusteRetiroFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Key() => Guid.NewGuid().ToString("N");
    private OperationsActor Manager => new(app.TenantA, "manager-a");
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private async Task<JsonElement> Command(Func<EnvirapidoOperationsService, Task<object>> action)
    {
        JsonElement result = default;
        await app.InScopeAsync(async sp => result = JsonSerializer.SerializeToElement(
            await action(sp.GetRequiredService<EnvirapidoOperationsService>()), Json));
        return result;
    }
    private async Task<RetiroParadaDto> CollectedRetiro(bool pilotRequest = false)
    {
        RetiroParadaDto result = null!;
        await app.InScopeAsync(async sp =>
        {
            var core = sp.GetRequiredService<IOrdenService>();
            var order = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
            await core.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            await core.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key());
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var stop = await db.ParadasEntrega.SingleAsync(p => p.OrdenEntregaId == order.OrdenId);
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var actor = pilotRequest ? new OperationsActor(app.TenantA, "pilot-a", app.PilotA) : Manager;
            var ack = JsonSerializer.SerializeToElement(await service.SolicitarRetiroParadasAsync(actor,
                new(order.OrdenId, [new(stop.Id, stop.RevisionAsignacion)], "Trabajo interrumpido tras recoger", Key())), Json);
            result = ack.GetProperty("gestiones")[0].Deserialize<RetiroParadaDto>(Json)!;
        });
        return result;
    }

    [Fact]
    public async Task Manual_retirement_adjustment_pays_origin_without_inventing_completed_deliveries_or_using_cash()
    {
        var retiro = await CollectedRetiro();
        var request = new RegistrarAjusteRetiroRequest(125.50m, "Recogida y custodia hasta retiro autorizado", retiro.Version, Key());
        var first = await Command(s => s.RegistrarAjusteRetiroAsync(Manager, retiro.Id, request));
        var replay = await Command(s => s.RegistrarAjusteRetiroAsync(Manager, retiro.Id, request));
        Assert.Equal(first.GetRawText(), replay.GetRawText());
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var settlement = await db.LiquidacionesOperativas.Include(l => l.Movimientos).SingleAsync();
            Assert.Equal(app.PilotA, settlement.PilotoId); Assert.Null(settlement.TiendaId);
            Assert.Equal(EstadoLiquidacion.EnRevision, settlement.Estado); Assert.False(settlement.Conciliada);
            Assert.Equal(0, settlement.Entregas); Assert.Equal(125.50m, settlement.Ajustes); Assert.Equal(125.50m, settlement.Neto);
            Assert.Equal(0m, settlement.Servicios); Assert.Equal(0m, settlement.EfectivoCompensado);
            Assert.Null(settlement.PagadaAt); Assert.Null(settlement.FechaPago); Assert.Null(settlement.ReferenciaPago);
            var movement = Assert.Single(settlement.Movimientos);
            Assert.Equal($"retiro:{retiro.Id}:ajuste", movement.ClaveOrigen); Assert.Equal("Ajustes", movement.Concepto);
            Assert.Equal(retiro.OrdenId, movement.OrdenEntregaId); Assert.Equal(retiro.ParadaId, movement.ParadaEntregaId);
            Assert.Null(movement.ReglaId); Assert.Equal("manager-a", movement.ActorId);
            Assert.Empty(await db.CobrosOperativos.ToListAsync());
            Assert.Equal(EstadoParadaEntrega.Pendiente, (await db.ParadasEntrega.SingleAsync()).Estado);
            Assert.NotEqual(EstadoOrden.Entregada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            Assert.False((await db.CodigosEntrega.SingleAsync(c => c.Tipo == TipoCodigo.C_Finalizacion)).Usado);
            Assert.Equal(1, await db.OperacionesEnvirapido.CountAsync(o => o.Tipo == "AjusteRetiro"));
        });
    }

    [Fact]
    public async Task Adjustment_requires_authorization_exact_tenant_and_version_and_rejects_a_second_origin_charge()
    {
        var retiro = await CollectedRetiro(pilotRequest: true);
        var pending = new RegistrarAjusteRetiroRequest(50m, "Recogida realizada", retiro.Version, Key());
        Assert.Equal(409, (await Assert.ThrowsAsync<RejectedOperationException>(() =>
            Command(s => s.RegistrarAjusteRetiroAsync(Manager, retiro.Id, pending)))).StatusCode);
        await Command(s => s.DecidirRetiroParadaAsync(Manager, retiro.Id,
            new("Aprobar", "Trabajo interrumpido confirmado", retiro.Version, Key())));
        var approved = (await Command(async s => (await s.ReasignacionesAsync(Manager)).Gestiones.Single(g => g.Id == retiro.Id)))
            .Deserialize<RetiroParadaDto>(Json)!;
        var request = pending with { Version = approved.Version, OperacionId = Key() };
        await app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            var managerB = new IdentityUser { Id = "manager-b", UserName = "manager-b@example.test", Email = "manager-b@example.test" };
            Assert.True((await users.CreateAsync(managerB)).Succeeded);
            Assert.True((await users.AddToRoleAsync(managerB, "Gestor")).Succeeded);
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.ClientesApp.SingleAsync(t => t.Id == app.TenantB)).GestorIdentityUserId = managerB.Id;
            await db.SaveChangesAsync();
        });
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.RegistrarAjusteRetiroAsync(
            new(app.TenantA, "pilot-a", app.PilotA), retiro.Id, request)))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationsException>(() => Command(s => s.RegistrarAjusteRetiroAsync(
            new(app.TenantB, "manager-b"), retiro.Id, request)))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.RegistrarAjusteRetiroAsync(
            Manager, retiro.Id, request with { Importe = 0m, OperacionId = Key() })))).StatusCode);
        var first = await Command(s => s.RegistrarAjusteRetiroAsync(Manager, retiro.Id, request));
        var duplicate = request with { Version = first.GetProperty("retiroVersion").GetString()!, OperacionId = Key() };
        var error = await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.RegistrarAjusteRetiroAsync(Manager, retiro.Id, duplicate)));
        Assert.Equal(409, error.StatusCode); Assert.Contains("ya tiene un ajuste", error.Message);
        Assert.Equal(409, (await Assert.ThrowsAsync<RejectedOperationException>(() => Command(s => s.RegistrarAjusteRetiroAsync(
            Manager, retiro.Id, request with { OperacionId = Key() })))).StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.LiquidacionesOperativas.CountAsync()); Assert.Equal(1, await db.MovimientosOperativos.CountAsync());
            Assert.False(await db.LiquidacionesOperativas.AnyAsync(l => l.ClienteAppId == app.TenantB));
        });
    }
}
