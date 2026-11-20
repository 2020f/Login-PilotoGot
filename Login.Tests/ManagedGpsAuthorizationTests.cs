using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ManagedGpsAuthorizationTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Key() => Guid.NewGuid().ToString("N");
    private OperationsActor Manager => new(app.TenantA, "manager-a");

    public async Task InitializeAsync()
    {
        await app.SeedAsync();
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            foreach (var pilot in await db.Pilotos.ToListAsync()) pilot.EnLinea = false;
            await db.SaveChangesAsync();
        });
    }
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    private async Task<OrdenCreadaResult> CreateAsync(int stops = 1, bool picked = false)
    {
        OrdenCreadaResult order = null!;
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrdenService>();
            order = await service.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key(),
                destinatarios: Enumerable.Range(0, stops).Select(_ => new OrdenDestinatarioRequest(app.CustomerA)).ToArray());
            await service.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            if (picked) await service.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key());
        });
        return order;
    }
    private async Task<List<RevisionParadaRequest>> SelectAsync(int orderId, params int[] ids)
    {
        List<RevisionParadaRequest> result = [];
        await app.InScopeAsync(async sp => result = await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega
            .Where(p => p.ClienteAppId == app.TenantA && p.OrdenEntregaId == orderId && ids.Contains(p.Id))
            .Select(p => new RevisionParadaRequest(p.Id, p.RevisionAsignacion)).ToListAsync());
        Assert.Equal(ids.Length, result.Count);
        return result;
    }
    private async Task<JsonElement> Command(Func<EnvirapidoOperationsService, Task<object>> action)
    {
        JsonElement result = default;
        await app.InScopeAsync(async sp => result = JsonSerializer.SerializeToElement(
            await action(sp.GetRequiredService<EnvirapidoOperationsService>()), Json));
        return result;
    }
    private async Task<RetiroParadaDto> TransferAsync(OrdenCreadaResult order)
    {
        var selected = await SelectAsync(order.OrdenId, order.Paradas[0].ParadaId);
        var ack = await Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, selected, app.PilotA2, "Traspaso GPS de prueba", Key())));
        return ack.GetProperty("gestiones")[0].Deserialize<RetiroParadaDto>(Json)!;
    }
    private async Task AssertGpsAsync(string user, bool allowed)
    {
        using var client = await app.LoginAsync(user);
        client.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "4");
        using var activity = await client.GetAsync("/api/piloto/operaciones/actividad");
        Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
        var active = await activity.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(allowed, active.GetProperty("requiereGps").GetBoolean());
        Assert.Equal(!allowed, active.GetProperty("autorizacionGpsHasta").ValueKind == JsonValueKind.Null);
        using var snapshot = await client.GetAsync("/api/piloto/operaciones");
        Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);
        Assert.Equal(allowed, (await snapshot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requiereGps").GetBoolean());
        var request = new EnviarGpsRequest(Key(), [new(18.48, -69.91, app.Clock.GetUtcNow().UtcDateTime, 1, 10)], Key());
        using var sent = await client.PostAsJsonAsync("/api/piloto/operaciones/ubicaciones", request);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, sent.StatusCode);
        if (allowed)
        {
            var ack = await sent.Content.ReadAsStringAsync();
            Assert.Equal(1, JsonDocument.Parse(ack).RootElement.GetProperty("recibidas").GetInt32());
            using var replay = await client.PostAsJsonAsync("/api/piloto/operaciones/ubicaciones", request);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(ack, await replay.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Split_order_without_header_pilot_authorizes_both_offline_pilots_only(bool picked)
    {
        var order = await CreateAsync(2, picked);
        await TransferAsync(order);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Null((await db.OrdenesEntrega.SingleAsync()).PilotoId);
            Assert.All(await db.Pilotos.ToListAsync(), p => Assert.False(p.EnLinea));
        });
        await AssertGpsAsync("pilot-a", true);
        await AssertGpsAsync("pilot-a2", true);
        await AssertGpsAsync("pilot-b", false);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.PosicionesPilotosOperativas.CountAsync());
            Assert.All(await db.PosicionesPilotosOperativas.ToListAsync(), p => Assert.Equal(app.TenantA, p.ClienteAppId));
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var forged = new OperationsActor(app.TenantA, "pilot-b", app.PilotB);
            Assert.Equal(403, (await Assert.ThrowsAsync<OperationsException>(() => service.ActividadAsync(forged))).StatusCode);
        });
    }

    [Fact]
    public async Task Original_custodian_keeps_gps_until_receiver_confirms_then_loses_authorization()
    {
        var order = await CreateAsync(picked: true);
        var pending = await TransferAsync(order);
        Assert.Equal("PendienteRecepcion", pending.Estado);
        await AssertGpsAsync("pilot-a", true);
        await AssertGpsAsync("pilot-a2", true);
        await Command(s => s.ConfirmarRecepcionParadaAsync(new(app.TenantA, "pilot-a2", app.PilotA2),
            pending.Id, new(pending.Version, "Recepcion fisica simulada", Key())));
        await AssertGpsAsync("pilot-a", false);
        await AssertGpsAsync("pilot-a2", true);
    }

    [Fact]
    public async Task Cancelled_order_custody_still_authorizes_gps_until_return_is_received()
    {
        var order = await CreateAsync(picked: true);
        var selected = await SelectAsync(order.OrdenId, order.Paradas[0].ParadaId);
        await Command(s => s.CancelarParadasAsync(Manager, new(order.OrdenId, selected, "Cancelar y devolver", Key())));
        RetiroParadaDto pending = null!;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Cancelada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            pending = (await sp.GetRequiredService<EnvirapidoOperationsService>().ReasignacionesAsync(Manager))
                .Gestiones.Single(g => g.Abierta);
        });
        await AssertGpsAsync("pilot-a", true);
        await AssertGpsAsync("pilot-a2", false);
        var returning = (await Command(s => s.ProgramarRetornoParadaAsync(Manager, pending.Id,
            new(pending.Version, Key(), app.ShopA2)))).Deserialize<RetiroParadaDto>(Json)!;
        await Command(s => s.ConfirmarRecepcionParadaAsync(Manager, returning.Id,
            new(returning.Version, "Centro confirma recepcion", Key())));
        await AssertGpsAsync("pilot-a", false);
    }

    [Fact]
    public async Task Delivered_managed_history_does_not_authorize_gps_for_previous_participants()
    {
        var order = await CreateAsync(picked: true);
        var pending = await TransferAsync(order);
        await Command(s => s.ConfirmarRecepcionParadaAsync(new(app.TenantA, "pilot-a2", app.PilotA2),
            pending.Id, new(pending.Version, "Recepcion fisica", Key())));
        var stop = (await SelectAsync(order.OrdenId, order.Paradas[0].ParadaId)).Single();
        await app.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IOrdenService>().ConfirmarEntregaParadaAsync(order.OrdenId, stop.ParadaId,
                order.Paradas[0].CodigoC, "pilot-a2", Key(), stop.RevisionAsignacion);
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Entregada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            Assert.Equal(app.PilotA2, (await db.ParadasEntrega.SingleAsync()).PilotoEntregaId);
        });
        await AssertGpsAsync("pilot-a", false);
        await AssertGpsAsync("pilot-a2", false);
    }

    [Fact]
    public async Task Legacy_manual_pickup_and_delivery_keep_existing_gps_lifecycle()
    {
        var order = await CreateAsync();
        await AssertGpsAsync("pilot-a", true);
        await AssertGpsAsync("pilot-a2", false);
        await app.InScopeAsync(async sp => await sp.GetRequiredService<IOrdenService>()
            .ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key()));
        await AssertGpsAsync("pilot-a", true);
        await app.InScopeAsync(async sp => await sp.GetRequiredService<IOrdenService>()
            .ConfirmarEntregaParadaAsync(order.OrdenId, order.Paradas[0].ParadaId, order.Paradas[0].CodigoC, "pilot-a", Key()));
        await AssertGpsAsync("pilot-a", false);
    }
}
