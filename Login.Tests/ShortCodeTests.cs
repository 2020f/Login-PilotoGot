using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ShortCodeTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task<OrdenCreadaResult> CreateAsync(int? tenant = null, int? shop = null, int? customer = null)
    {
        OrdenCreadaResult result = null!;
        await _app.InScopeAsync(async sp => result = await sp.GetRequiredService<IOrdenService>()
            .CrearOrdenAsync(tenant ?? _app.TenantA, shop ?? _app.ShopA, customer ?? _app.CustomerA));
        return result;
    }

    private async Task SetCodesAsync(int id, string b = "0012", string c = "0034")
        => await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var codes = await db.CodigosEntrega.Where(x => x.OrdenEntregaId == id).ToListAsync();
            codes.Single(x => x.Tipo == TipoCodigo.B_Recoleccion).Codigo = b;
            codes.Single(x => x.Tipo == TipoCodigo.C_Finalizacion).Codigo = c;
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Concurrent_new_codes_are_four_digits_and_reserved_per_tenant()
    {
        var orders = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => CreateAsync()));
        foreach (var order in orders)
            foreach (var code in new[] { order.CodigoA, order.CodigoB, order.CodigoC })
                Assert.Matches("^[0-9]{4}$", code!);
        Assert.Equal(12, orders.Select(o => o.CodigoA).Distinct().Count());
        Assert.Equal(12, orders.Select(o => o.CodigoB).Distinct().Count());
        Assert.Equal(12, orders.Select(o => o.CodigoC).Distinct().Count());
        var first = orders.Single(o => o.NumeroOrdenA == 1);
        Assert.Equal("0001", first.CodigoA);
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var qr = await shop.GetFromJsonAsync<JsonElement>($"/api/tienda/ordenes/{first.OrdenId}/qr");
        Assert.Equal("0001", qr.GetProperty("codigoA").GetString());
        var offers = await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/ordenes-auto");
        Assert.Contains(offers.EnumerateArray(), x => x.GetProperty("codigoA").GetString() == "0001");
    }

    [Fact]
    public async Task Same_digits_in_two_tenants_require_the_right_order_and_pilot()
    {
        var a = await CreateAsync();
        var b = await CreateAsync(_app.TenantB, _app.ShopB, _app.CustomerB);
        await SetCodesAsync(a.OrdenId);
        await SetCodesAsync(b.OrdenId);
        Assert.Equal(a.CodigoA, b.CodigoA);
        using var pa = await _app.LoginAsync("pilot-a");
        using var pb = await _app.LoginAsync("pilot-b");
        (await pa.PostAsync($"/api/piloto/ordenes-auto/{a.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        (await pb.PostAsync($"/api/piloto/ordenes-auto/{b.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await pa.PostAsJsonAsync("/api/piloto/confirmar-recolecta",
            new { ordenId = b.OrdenId, codigoB = "0012" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pa.PostAsJsonAsync("/api/piloto/confirmar-recolecta",
            new { codigoB = "0012" })).StatusCode);
        foreach (var (client, order) in new[] { (pa, a), (pb, b) })
        {
            (await client.PostAsJsonAsync("/api/piloto/confirmar-recolecta",
                new { ordenId = order.OrdenId, codigoB = "0012" })).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync("/api/piloto/confirmar-entrega",
                new { ordenId = order.OrdenId, codigoB = "0012", codigoC = "0034" })).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Completed_codes_can_be_reused_without_accepting_a_stale_order_request()
    {
        var first = await CreateAsync();
        await SetCodesAsync(first.OrdenId);
        await _app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrdenService>();
            await service.AsignarPilotoAsync("0012", _app.PilotA, "manager-a", "Gestor", first.OrdenId);
            await service.ConfirmarRecolectaAsync("0012", "pilot-a", "Piloto", first.OrdenId);
            await service.ConfirmarEntregaAsync("0012", "0034", "pilot-a", "Piloto", first.OrdenId);
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.All(await db.CodigosEntrega.Where(c => c.OrdenEntregaId == first.OrdenId).ToListAsync(), c => Assert.False(c.Reservado));
            (await db.ClientesApp.FindAsync(_app.TenantA))!.OrdenSeq = 10000;
            await db.SaveChangesAsync();
        });
        var next = await CreateAsync();
        Assert.Equal(first.CodigoA, next.CodigoA);
        Assert.NotEqual(first.OrdenId, next.OrdenId);
        Assert.Equal(10001, next.NumeroOrdenA);
        await SetCodesAsync(next.OrdenId);
        await _app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrdenService>();
            await service.AsignarPilotoAsync("0012", _app.PilotA, "manager-a", "Gestor", next.OrdenId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmarRecolectaAsync("0012", "pilot-a", "Piloto", first.OrdenId));
            await service.ConfirmarRecolectaAsync("0012", "pilot-a", "Piloto", next.OrdenId);
            await service.ConfirmarEntregaAsync("0012", "0034", "pilot-a", "Piloto", next.OrdenId);
            Assert.Equal(2, await sp.GetRequiredService<ApplicationDbContext>().OrdenesEntrega.CountAsync(o => o.Estado == EstadoOrden.Entregada));
        });
    }

    [Fact]
    public async Task Active_legacy_A_is_reserved_and_legacy_codes_still_complete()
    {
        var old = new OrdenEntrega
        {
            ClienteAppId = _app.TenantA, TiendaId = _app.ShopA, UsuarioFinalId = _app.CustomerA,
            NumeroOrdenA = 1, PilotoId = _app.PilotA, Estado = EstadoOrden.Asignada,
            Codigos = new List<CodigoEntrega>
            {
                new() { ClienteAppId = _app.TenantA, Tipo = TipoCodigo.B_Recoleccion, Codigo = "ABC1234567" },
                new() { ClienteAppId = _app.TenantA, Tipo = TipoCodigo.C_Finalizacion, Codigo = "DEF7654321" }
            }
        };
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.Add(old);
            (await db.ClientesApp.FindAsync(_app.TenantA))!.OrdenSeq = 10000;
            await db.SaveChangesAsync();
        });
        var next = await CreateAsync();
        Assert.Equal("0002", next.CodigoA);
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var qr = await shop.GetFromJsonAsync<JsonElement>($"/api/tienda/ordenes/{old.Id}/qr");
        Assert.Equal(JsonValueKind.Null, qr.GetProperty("codigoA").ValueKind);
        Assert.Equal(1, qr.GetProperty("numeroOrdenA").GetInt32());
        Assert.Equal("ABC1234567", qr.GetProperty("codigoB").GetString());
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { codigoB = "ABC1234567" })).EnsureSuccessStatusCode();
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega", new { codigoB = "ABC1234567", codigoC = "DEF7654321" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Database_rejects_duplicate_active_codes_within_one_tenant()
    {
        var first = await CreateAsync();
        var second = await CreateAsync();
        await SetCodesAsync(first.OrdenId);
        await Assert.ThrowsAsync<DbUpdateException>(() => SetCodesAsync(second.OrdenId));
    }

    [Fact]
    public async Task Code_attempts_are_limited_without_blocking_order_refresh()
    {
        using var pilot = await _app.LoginAsync("pilot-a");
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta",
                new { ordenId = 99999, codigoB = "0000" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega",
            new { ordenId = 99999, codigoB = "0000", codigoC = "0000" })).StatusCode);
        (await pilot.GetAsync("/api/piloto/mis-ordenes")).EnsureSuccessStatusCode();
    }
}
