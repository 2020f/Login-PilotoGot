using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ManagedPilotLifecycleTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Key() => Guid.NewGuid().ToString("N");
    private OperationsActor Manager => new(app.TenantA, "manager-a");
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private async Task<JsonElement> Command(Func<EnvirapidoOperationsService, Task<object>> action)
    {
        JsonElement ack = default;
        await app.InScopeAsync(async sp => ack = JsonSerializer.SerializeToElement(
            await action(sp.GetRequiredService<EnvirapidoOperationsService>()), Json));
        return ack;
    }
    private async Task<OrdenCreadaResult> CreateAsync(int count, bool pickup)
    {
        OrdenCreadaResult order = null!;
        await app.InScopeAsync(async sp =>
        {
            var core = sp.GetRequiredService<IOrdenService>();
            order = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key(),
                destinatarios: Enumerable.Range(0, count).Select(_ => new OrdenDestinatarioRequest(app.CustomerA)).ToArray());
            await core.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            if (pickup) await core.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key());
        });
        return order;
    }
    private async Task<List<RevisionParadaRequest>> SelectionAsync(int orderId, int stopId)
    {
        List<RevisionParadaRequest> selected = [];
        await app.InScopeAsync(async sp => selected = await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega
            .Where(p => p.OrdenEntregaId == orderId && p.Id == stopId)
            .Select(p => new RevisionParadaRequest(p.Id, p.RevisionAsignacion)).ToListAsync());
        return selected;
    }
    private static async Task<HttpResponseMessage> PilotPostAsync(HttpClient manager, string action, int pilotId, bool active = false)
    {
        var page = await manager.GetStringAsync($"/Supervisor/EditarPiloto?id={pilotId}");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        return await manager.PostAsync("/Supervisor/" + action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = pilotId.ToString(), ["Nombre"] = "Piloto A", ["Telefono"] = "5550011",
            ["Activo"] = active.ToString(), ["EmailPiloto"] = "pilot-a@example.test",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
        }));
    }
    private async Task AssertAllDeactivationPathsBlocked(HttpClient manager)
    {
        foreach (var action in new[] { "DesactivarPiloto", "EliminarPiloto", "EditarPiloto" })
        {
            using var response = await PilotPostAsync(manager, action, app.PilotA);
            if (action == "EditarPiloto")
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("paquetes en custodia", await response.Content.ReadAsStringAsync());
            }
            else
            {
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Contains("error=", response.Headers.Location!.OriginalString);
            }
            await app.InScopeAsync(async sp => Assert.True((await sp.GetRequiredService<ApplicationDbContext>()
                .Pilotos.AsNoTracking().SingleAsync(p => p.Id == app.PilotA)).Activo));
        }
    }

    [Fact]
    public async Task Split_parent_without_pilot_still_blocks_deactivation_and_edit_keeps_busy_state()
    {
        var order = await CreateAsync(2, pickup: false);
        var selected = await SelectionAsync(order.OrdenId, order.Paradas[1].ParadaId);
        await Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, selected, app.PilotA2, "Dividir trabajo antes de recoger", Key())));
        using var manager = await app.LoginWebAsync("manager-a");
        await AssertAllDeactivationPathsBlocked(manager);
        using var edit = await PilotPostAsync(manager, "EditarPiloto", app.PilotA, active: true);
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Null((await db.OrdenesEntrega.SingleAsync()).PilotoId);
            Assert.Equal(EstadoPiloto.Ocupado, (await db.Pilotos.SingleAsync(p => p.Id == app.PilotA)).Estado);
            var policy = sp.GetRequiredService<QuotaPolicyService>();
            Assert.True(await policy.HasOpenPilotWorkAsync(app.TenantA, app.PilotA));
            Assert.True(await policy.HasOpenPilotWorkAsync(app.TenantA, app.PilotA2));
            Assert.False(await policy.HasOpenPilotWorkAsync(app.TenantB, app.PilotA));
        });
    }

    [Fact]
    public async Task Cancelled_delivery_blocks_its_custodian_until_physical_receipt_and_pending_receiver_stays_protected()
    {
        var order = await CreateAsync(1, pickup: true);
        var selected = await SelectionAsync(order.OrdenId, order.Paradas[0].ParadaId);
        var transfer = await Command(s => s.AsignarParadasAsync(Manager,
            new(order.OrdenId, selected, app.PilotA2, "Transferencia física pendiente", Key())));
        await app.InScopeAsync(async sp => Assert.True(await sp.GetRequiredService<QuotaPolicyService>()
            .HasOpenPilotWorkAsync(app.TenantA, app.PilotA2)));
        selected = await SelectionAsync(order.OrdenId, order.Paradas[0].ParadaId);
        await Command(s => s.CancelarParadasAsync(Manager, new(order.OrdenId, selected, "Cliente canceló", Key())));
        using var manager = await app.LoginWebAsync("manager-a");
        await AssertAllDeactivationPathsBlocked(manager);
        RetiroParadaDto pending = null!;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Cancelada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            Assert.Equal(EstadoParadaEntrega.Cancelada, (await db.ParadasEntrega.SingleAsync()).Estado);
            Assert.Equal(app.PilotA, (await db.ParadasEntrega.SingleAsync()).PilotoCustodioId);
            pending = (await sp.GetRequiredService<EnvirapidoOperationsService>().ReasignacionesAsync(Manager))
                .Gestiones.Single(g => g.Abierta);
        });
        var planned = (await Command(s => s.ProgramarRetornoParadaAsync(Manager, pending.Id,
            new(pending.Version, Key(), app.ShopA2)))).Deserialize<RetiroParadaDto>(Json)!;
        await Command(s => s.ConfirmarRecepcionParadaAsync(Manager, planned.Id,
            new(planned.Version, "Recepción física completada", Key())));
        await app.InScopeAsync(async sp =>
        {
            var policy = sp.GetRequiredService<QuotaPolicyService>();
            Assert.False(await policy.HasOpenPilotWorkAsync(app.TenantA, app.PilotA));
            Assert.False(await policy.HasOpenPilotWorkAsync(app.TenantA, app.PilotA2));
        });
        using var deactivate = await PilotPostAsync(manager, "DesactivarPiloto", app.PilotA);
        Assert.Equal(HttpStatusCode.Redirect, deactivate.StatusCode);
        Assert.DoesNotContain("error=", deactivate.Headers.Location!.OriginalString);
        await app.InScopeAsync(async sp => Assert.False((await sp.GetRequiredService<ApplicationDbContext>()
            .Pilotos.SingleAsync(p => p.Id == app.PilotA)).Activo));
    }

    [Fact]
    public async Task A_cancelled_orders_return_destination_cannot_be_deactivated_until_its_store_receives_the_package()
    {
        var order = await CreateAsync(1, pickup: true);
        var selected = await SelectionAsync(order.OrdenId, order.Paradas[0].ParadaId);
        await Command(s => s.CancelarParadasAsync(Manager, new(order.OrdenId, selected, "Cancelar y retornar", Key())));
        var pending = (await Command(async s => (await s.ReasignacionesAsync(Manager)).Gestiones.Single(g => g.Abierta)))
            .Deserialize<RetiroParadaDto>(Json)!;
        var planned = (await Command(s => s.ProgramarRetornoParadaAsync(Manager, pending.Id,
            new(pending.Version, Key(), app.ShopA2)))).Deserialize<RetiroParadaDto>(Json)!;
        using var manager = await app.LoginWebAsync("manager-a");
        foreach (var action in new[] { "DesactivarTienda", "EditarTienda" })
        {
            var page = await manager.GetStringAsync($"/Supervisor/EditarTienda?id={app.ShopA2}");
            var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(token.Success);
            using var response = await manager.PostAsync("/Supervisor/" + action, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Id"] = app.ShopA2.ToString(), ["Nombre"] = "A2", ["Direccion"] = "Calle A2", ["Telefono"] = "5550012",
                ["Activo"] = "false", ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
            }));
            if (action == "EditarTienda")
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("recepciones de paquetes pendientes", await response.Content.ReadAsStringAsync());
            }
            else
            {
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Contains("error=", response.Headers.Location!.OriginalString);
            }
            await app.InScopeAsync(async sp =>
            {
                var db = sp.GetRequiredService<ApplicationDbContext>();
                Assert.True((await db.Tiendas.AsNoTracking().SingleAsync(t => t.Id == app.ShopA2)).Activo);
                Assert.Equal(EstadoOrden.Cancelada, (await db.OrdenesEntrega.SingleAsync()).Estado);
                Assert.Equal(app.PilotA, (await db.ParadasEntrega.SingleAsync()).PilotoCustodioId);
                // The origin is historical here; the authorized receiver is A2.
                await sp.GetRequiredService<QuotaPolicyService>().EnsureNoOpenStoreOrdersAsync(app.TenantA, app.ShopA);
            });
        }
        using var store = await app.LoginAsync("multi");
        store.DefaultRequestHeaders.Add("X-Tienda-Id", app.ShopA2.ToString());
        using var receipt = await store.PostAsJsonAsync($"/api/tienda/recepciones/{planned.Id}?tiendaId={app.ShopA2}",
            new ConfirmarRecepcionParadaRequest(planned.Version, "Comercio recibió físicamente la devolución", Key()));
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var after = await manager.GetStringAsync($"/Supervisor/EditarTienda?id={app.ShopA2}");
        var csrf = Regex.Match(after, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(csrf.Success);
        using var deactivate = await manager.PostAsync("/Supervisor/DesactivarTienda", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = app.ShopA2.ToString(), ["__RequestVerificationToken"] = WebUtility.HtmlDecode(csrf.Groups[1].Value)
        }));
        Assert.Equal(HttpStatusCode.Redirect, deactivate.StatusCode);
        Assert.DoesNotContain("error=", deactivate.Headers.Location!.OriginalString);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.False((await db.Tiendas.SingleAsync(t => t.Id == app.ShopA2)).Activo);
            Assert.Null((await db.ParadasEntrega.SingleAsync()).PilotoCustodioId);
        });
    }
}
