using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class OrderFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static async Task<JsonElement> CreateAsync(HttpClient shop, int customer)
    {
        var response = await shop.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = customer, notaPedido = "QA" });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Two_successive_auto_orders_finish_without_another_login()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        for (var n = 1; n <= 2; n++)
        {
            var order = await CreateAsync(shop, _app.CustomerA);
            var id = order.GetProperty("ordenId").GetInt32();
            Assert.Equal(n, order.GetProperty("numeroOrdenA").GetInt32());
            var offers = await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/ordenes-auto");
            Assert.Contains(offers.EnumerateArray(), o => o.GetProperty("ordenId").GetInt32() == id);
            (await pilot.PostAsync($"/api/piloto/ordenes-auto/{id}/aceptar", null)).EnsureSuccessStatusCode();
            Assert.Empty((await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/ordenes-auto")).EnumerateArray());
            var b = order.GetProperty("codigoB").GetString();
            var c = order.GetProperty("codigoC").GetString();
            (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { ordenId = id, codigoB = b })).EnsureSuccessStatusCode();
            (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega", new { ordenId = id, codigoB = b, codigoC = c })).EnsureSuccessStatusCode();
            Assert.Empty((await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/mis-ordenes")).EnumerateArray());
        }
    }

    [Fact]
    public async Task Manual_assignments_are_visible_with_the_same_bearer_token()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        for (var n = 1; n <= 2; n++)
        {
            var order = await CreateAsync(shop, _app.CustomerA);
            await _app.InScopeAsync(sp => sp.GetRequiredService<IOrdenService>().AsignarPilotoAsync(order.GetProperty("codigoB").GetString()!, _app.PilotA, "manager-a", "Gestor", order.GetProperty("ordenId").GetInt32()));
            Assert.Equal(n, (await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/mis-ordenes")).GetArrayLength());
        }
    }

    [Fact]
    public async Task Another_tenant_cannot_accept_or_reject_an_order_by_id()
    {
        using var shop = await _app.LoginAsync("shop-b");
        using var pilot = await _app.LoginAsync("pilot-a");
        var order = await CreateAsync(shop, _app.CustomerB);
        var id = order.GetProperty("ordenId").GetInt32();
        Assert.Empty((await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/ordenes-auto")).EnumerateArray());
        Assert.False((await pilot.PostAsync($"/api/piloto/ordenes-auto/{id}/aceptar", null)).IsSuccessStatusCode);
        Assert.False((await pilot.PostAsync($"/api/piloto/ordenes-auto/{id}/rechazar", null)).IsSuccessStatusCode);
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Null((await db.OrdenesEntrega.FindAsync(id))!.PilotoId);
            Assert.False(await db.OrdenesAutoRechazo.AnyAsync());
        });
    }

    [Fact]
    public async Task Offline_pilot_cannot_accept_by_id()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var order = await CreateAsync(shop, _app.CustomerA);
        (await pilot.PostAsJsonAsync("/api/piloto/estado", new { enLinea = false })).EnsureSuccessStatusCode();
        Assert.False((await pilot.PostAsync($"/api/piloto/ordenes-auto/{order.GetProperty("ordenId")}/aceptar", null)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Multi_store_user_can_switch_and_only_read_selected_store()
    {
        using var multi = await _app.LoginAsync("multi");
        (await multi.GetAsync("/api/tienda/tiendas")).EnsureSuccessStatusCode();
        var response = await multi.PostAsJsonAsync("/api/tienda/tienda-activa", new { tiendaId = _app.ShopA2 });
        response.EnsureSuccessStatusCode();
        Assert.Equal(_app.ShopA2, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
        await CreateAsync(multi, _app.CustomerA2);
        Assert.False((await multi.PostAsJsonAsync("/api/tienda/tienda-activa", new { tiendaId = _app.ShopB })).IsSuccessStatusCode);
        (await multi.PostAsJsonAsync("/api/tienda/tienda-activa", new { tiendaId = _app.ShopA })).EnsureSuccessStatusCode();
        Assert.Empty((await multi.GetFromJsonAsync<JsonElement>("/api/tienda/ordenes")).EnumerateArray());
    }

    [Fact]
    public async Task Suspended_tenant_is_forbidden_on_mobile_endpoints()
    {
        using var pilot = await _app.LoginAsync("pilot-a");
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.ClientesApp.FindAsync(_app.TenantA))!.Estado = EstadoClienteApp.Suspendido;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await pilot.GetAsync("/api/piloto/mis-ordenes")).StatusCode);
    }

    [Fact]
    public async Task Simultaneous_creations_keep_unique_sequence_and_monthly_limit()
    {
        using var shop = await _app.LoginAsync("shop-a");
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var plan = await db.Planes.SingleAsync();
            plan.MaxOrdenesPorMes = 3;
            await db.SaveChangesAsync();
        });
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => shop.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA })));
        Assert.Equal(3, responses.Count(r => r.IsSuccessStatusCode));
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(new[] { 1, 2, 3 }, await db.OrdenesEntrega.OrderBy(o => o.NumeroOrdenA).Select(o => o.NumeroOrdenA).ToArrayAsync());
            Assert.Equal(3, (await db.ClientesApp.FindAsync(_app.TenantA))!.OrdenSeq);
        });
    }

    [Fact]
    public async Task One_pilot_can_accept_distinct_auto_orders_concurrently_without_duplicate_assignments()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var first = await CreateAsync(shop, _app.CustomerA);
        var second = await CreateAsync(shop, _app.CustomerA);
        var responses = await Task.WhenAll(new[] { first, second }.Select(o =>
            pilot.PostAsync($"/api/piloto/ordenes-auto/{o.GetProperty("ordenId")}/aceptar", null)));
        Assert.All(responses, r => r.EnsureSuccessStatusCode());
        Assert.Equal(2, (await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/mis-ordenes")).GetArrayLength());
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var orders = await db.OrdenesEntrega.ToListAsync();
            Assert.Equal(2, orders.Count);
            Assert.All(orders, o => { Assert.Equal(_app.PilotA, o.PilotoId); Assert.Equal(EstadoOrden.Asignada, o.Estado); });
            foreach (var order in orders)
                Assert.Equal(1, await db.HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == order.Id && h.TipoEvento == TipoEventoOrden.Asignada));
            Assert.Equal(4, await db.CodigosEntrega.CountAsync());
            Assert.False(await db.CodigosEntrega.AnyAsync(c => c.Usado));
        });
    }

    [Fact]
    public async Task Two_pilots_competing_for_one_offer_produce_one_assignment_and_history()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        using var other = await _app.LoginAsync("pilot-a2");
        var order = await CreateAsync(shop, _app.CustomerA);
        var id = order.GetProperty("ordenId").GetInt32();
        var responses = await Task.WhenAll(pilot.PostAsync($"/api/piloto/ordenes-auto/{id}/aceptar", null),
            other.PostAsync($"/api/piloto/ordenes-auto/{id}/aceptar", null));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        await _app.InScopeAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<ApplicationDbContext>()
            .HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == id && h.TipoEvento == TipoEventoOrden.Asignada)));
    }

    [Fact]
    public async Task Tenant_and_pilot_cannot_read_or_complete_other_orders_or_qr()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var otherShop = await _app.LoginAsync("shop-b");
        using var pilot = await _app.LoginAsync("pilot-a");
        using var otherPilot = await _app.LoginAsync("pilot-b");
        var order = await CreateAsync(shop, _app.CustomerA);
        var id = order.GetProperty("ordenId").GetInt32();
        var b = order.GetProperty("codigoB").GetString();
        var c = order.GetProperty("codigoC").GetString();
        Assert.False((await otherShop.GetAsync($"/api/tienda/ordenes/{id}/qr")).IsSuccessStatusCode);
        Assert.False((await otherShop.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA })).IsSuccessStatusCode);
        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{id}/aceptar", null)).EnsureSuccessStatusCode();
        var hidden = await pilot.GetFromJsonAsync<JsonElement>($"/api/piloto/ordenes/{id}");
        Assert.Equal("", hidden.GetProperty("usuarioFinalDireccion").GetString());
        Assert.False((await otherPilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { ordenId = id, codigoB = b })).IsSuccessStatusCode);
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { ordenId = id, codigoB = b })).EnsureSuccessStatusCode();
        Assert.False((await otherPilot.PostAsJsonAsync("/api/piloto/confirmar-entrega", new { ordenId = id, codigoB = b, codigoC = c })).IsSuccessStatusCode);
        var shown = await pilot.GetFromJsonAsync<JsonElement>($"/api/piloto/ordenes/{id}");
        Assert.Equal("Destino privado A", shown.GetProperty("usuarioFinalDireccion").GetString());
        Assert.Empty((await otherPilot.GetFromJsonAsync<JsonElement>("/api/piloto/mis-ordenes")).EnumerateArray());
    }

    [Fact]
    public async Task Manual_assignment_rejects_inactive_and_other_tenant_pilots()
    {
        using var shop = await _app.LoginAsync("shop-a");
        var order = await CreateAsync(shop, _app.CustomerA);
        var b = order.GetProperty("codigoB").GetString()!;
        await _app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrdenService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AsignarPilotoAsync(b, _app.PilotA, "pilot-b", "Gestor", order.GetProperty("ordenId").GetInt32()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AsignarPilotoAsync(b, _app.PilotB, "manager-a", "Gestor", order.GetProperty("ordenId").GetInt32()));
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.Pilotos.FindAsync(_app.PilotA))!.Activo = false;
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AsignarPilotoAsync(b, _app.PilotA, "manager-a", "Gestor", order.GetProperty("ordenId").GetInt32()));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_flow_in_one_service_scope_observes_each_committed_state(bool automatic)
    {
        await _app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrdenService>();
            var order = await service.CrearOrdenAsync(_app.TenantA, _app.ShopA, _app.CustomerA,
                actorIdentityUserId: "shop-a", actorRol: "Cliente");
            if (automatic)
                await service.AceptarOrdenAutoAsync(order.OrdenId, _app.PilotA, "pilot-a", "Piloto");
            else
                await service.AsignarPilotoAsync(order.CodigoB, _app.PilotA, "manager-a", "Gestor", order.OrdenId);
            await service.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId);
            await service.ConfirmarEntregaAsync(order.CodigoB, order.CodigoC, "pilot-a", "Piloto", order.OrdenId);
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Entregada, (await db.OrdenesEntrega.FindAsync(order.OrdenId))!.Estado);
            Assert.Equal(4, await db.HistorialOrdenes.CountAsync(h => h.OrdenEntregaId == order.OrdenId));
        });
    }
}
