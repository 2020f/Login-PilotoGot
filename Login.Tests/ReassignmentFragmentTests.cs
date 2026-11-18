using System.Net;
using System.Net.Http.Json;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ReassignmentFragmentTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<OrdenCreadaResult> AssignedOrder(bool foreign = false)
    {
        OrdenCreadaResult result = null!;
        await app.InScopeAsync(async sp =>
        {
            result = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(foreign ? app.TenantB : app.TenantA,
                foreign ? app.ShopB : app.ShopA, foreign ? app.CustomerB : app.CustomerA, operacionId: Key());
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var order = await db.OrdenesEntrega.SingleAsync(o => o.Id == result.OrdenId);
            order.PilotoId = foreign ? app.PilotB : app.PilotA; order.Estado = EstadoOrden.Asignada;
            order.AssignedAt = app.Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        });
        return result;
    }

    private async Task Request(OrdenCreadaResult order, string reason, bool foreign = false)
    {
        using var pilot = await app.LoginAsync(foreign ? "pilot-b" : "pilot-a");
        pilot.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "4");
        var response = await pilot.PostAsJsonAsync("/api/piloto/operaciones/retiros", new
        {
            ordenId = order.OrdenId, paradas = order.Paradas.Select(p => new { paradaId = p.ParadaId, revisionAsignacion = "" }),
            motivo = reason, operacionId = Key()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Idle_fragment_reads_show_a_new_pilot_request_with_the_same_manager_session_without_writing_operations()
    {
        var order = await AssignedOrder();
        using var manager = await app.LoginWebAsync("manager-a");
        var first = await manager.GetStringAsync("/Operaciones/ReasignacionesFragmento");
        Assert.Contains("data-reassignment-snapshot", first);
        Assert.DoesNotContain("NEW_PILOT_REQUEST_FOR_FRAGMENT", first);
        await Request(order, "NEW_PILOT_REQUEST_FOR_FRAGMENT");
        int operations = 0, history = 0, cases = 0, codes = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            operations = await db.OperacionesEnvirapido.CountAsync(); history = await db.HistorialOrdenes.CountAsync();
            cases = await db.RetirosParadas.CountAsync(); codes = await db.CodigosEntrega.CountAsync();
        });
        var updated = await manager.GetAsync("/Operaciones/ReasignacionesFragmento");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.True(updated.Headers.CacheControl?.NoStore == true);
        var html = await updated.Content.ReadAsStringAsync();
        Assert.Contains("NEW_PILOT_REQUEST_FOR_FRAGMENT", html);
        Assert.Contains("evReassignmentData", html);
        Assert.Contains("data-updated=", html);
        Assert.DoesNotContain("<dialog", html);
        Assert.DoesNotContain("data-reassignment-form", html);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(operations, await db.OperacionesEnvirapido.CountAsync());
            Assert.Equal(history, await db.HistorialOrdenes.CountAsync());
            Assert.Equal(cases, await db.RetirosParadas.CountAsync());
            Assert.Equal(codes, await db.CodigosEntrega.CountAsync());
        });
    }

    [Fact]
    public async Task Fragment_requires_manager_role_and_preserves_tenant_boundaries_for_global_and_order_reads()
    {
        var foreign = await AssignedOrder(true);
        await Request(foreign, "FOREIGN_TENANT_REQUEST_MUST_STAY_PRIVATE", true);
        using var manager = await app.LoginWebAsync("manager-a");
        var own = await manager.GetStringAsync("/Operaciones/ReasignacionesFragmento");
        Assert.DoesNotContain("FOREIGN_TENANT_REQUEST_MUST_STAY_PRIVATE", own);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/Operaciones/ReasignacionesFragmento?ordenId={foreign.OrdenId}")).StatusCode);
        using var shop = await app.LoginWebAsync("shop-a");
        AssertDenied(await shop.GetAsync("/Operaciones/ReasignacionesFragmento"), shop.BaseAddress!);
        using var pilot = await app.LoginWebAsync("pilot-a");
        AssertDenied(await pilot.GetAsync("/Operaciones/ReasignacionesFragmento"), pilot.BaseAddress!);
    }

    private static void AssertDenied(HttpResponseMessage response, Uri origin)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden) return;
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var destination = new Uri(origin, response.Headers.Location);
        Assert.Equal(origin.Scheme, destination.Scheme); Assert.Equal(origin.Host, destination.Host);
        Assert.Equal("/Identity/Account/AccessDenied", destination.AbsolutePath);
    }
}
