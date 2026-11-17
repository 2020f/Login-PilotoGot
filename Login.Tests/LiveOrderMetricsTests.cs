using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class LiveOrderMetricsTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    private async Task<OrdenCreadaResult> Create(int tenant, int shop, int customer)
    {
        OrdenCreadaResult order = null!;
        await app.InScopeAsync(async services => order = await services.GetRequiredService<IOrdenService>()
            .CrearOrdenAsync(tenant, shop, customer, notaPedido: "QA estadísticas en vivo", operacionId: Key()));
        return order;
    }

    private Task Assign(OrdenCreadaResult order, int pilot)
        => app.InScopeAsync(services => services.GetRequiredService<IOrdenService>()
            .AsignarPilotoAsync(order.CodigoB, pilot, "manager-a", "Gestor", order.OrdenId));

    private async Task<HttpClient> Pilot(string identity = "pilot-a")
    {
        var client = await app.LoginAsync(identity);
        client.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "3");
        return client;
    }

    private static async Task Pickup(HttpClient pilot, OrdenCreadaResult order)
    {
        using var response = await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta",
            new { order.OrdenId, order.CodigoB, operacionId = Key() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task Deliver(HttpClient pilot, OrdenCreadaResult order)
    {
        var stop = Assert.Single(order.Paradas);
        using var response = await pilot.PostAsJsonAsync(
            $"/api/piloto/ordenes/{order.OrdenId}/paradas/{stop.ParadaId}/confirmar-entrega",
            new { stop.CodigoC, operacionId = Key() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string InCourseCard(string html)
        => Assert.Single(Regex.Matches(html, "<article\\b[^>]*>.*?</article>", RegexOptions.Singleline)
            .Cast<Match>().Select(match => match.Value),
            card => card.Contains("data-ev-value=\"enCurso\"", StringComparison.Ordinal));

    private static int BoundValue(string html, string key)
    {
        var matches = Regex.Matches(html,
            "<(?:strong|span)\\b[^>]*data-ev-value=\"" + Regex.Escape(key) + "\"[^>]*>([^<]*)</(?:strong|span)>");
        var match = Assert.Single(matches.Cast<Match>());
        return int.Parse(WebUtility.HtmlDecode(match.Groups[1].Value).Trim(), NumberStyles.Number, CultureInfo.CurrentCulture);
    }

    private static void AssertOrderCard(string html, int pickingUp, int onTheWay)
    {
        var card = InCourseCard(html);
        Assert.Equal(pickingUp + onTheWay, BoundValue(card, "enCurso"));
        var caption = Regex.Match(card, "<small\\b[^>]*>(.*?)</small>", RegexOptions.Singleline);
        Assert.True(caption.Success, "El desglose de En curso debe estar en la tarjeta.");
        // A fresh GET rendered the correct caption even before this regression
        // was fixed. The retained card must expose BOTH live bindings consumed
        // by updateSummary, because polling replaces only the orders fragment.
        Assert.Equal(pickingUp, BoundValue(caption.Groups[1].Value, "recogiendo"));
        Assert.Equal(onTheWay, BoundValue(caption.Groups[1].Value, "enRuta"));
        Assert.Contains("recogiendo", caption.Groups[1].Value);
        Assert.Contains("en ruta", caption.Groups[1].Value);
    }

    private static JsonElement Summary(string html)
    {
        var script = Regex.Match(html, "<script\\b[^>]*data-ev-live-summary[^>]*>(.*?)</script>", RegexOptions.Singleline);
        Assert.True(script.Success, "El fragmento debe incluir el resumen que consume la actualización en vivo.");
        using var document = JsonDocument.Parse(script.Groups[1].Value);
        return document.RootElement.Clone();
    }

    private static void AssertSummary(string html, int pickingUp, int onTheWay, int delivered)
    {
        var summary = Summary(html);
        Assert.Equal(pickingUp, summary.GetProperty("recogiendo").GetInt32());
        Assert.Equal(onTheWay, summary.GetProperty("enRuta").GetInt32());
        Assert.Equal(pickingUp + onTheWay, summary.GetProperty("enCurso").GetInt32());
        Assert.Equal(delivered, summary.GetProperty("entregados").GetInt32());
    }

    private static async Task AssertFragment(HttpClient manager, int pickingUp, int onTheWay, int delivered,
        string filter = "", int? foreignOrder = null)
    {
        var html = await manager.GetStringAsync("/Operaciones/Fragmento?seccion=Pedidos" + filter);
        AssertSummary(html, pickingUp, onTheWay, delivered);
        if (foreignOrder.HasValue) Assert.DoesNotContain($"data-order-id=\"{foreignOrder}\"", html);
        Assert.DoesNotContain("Destino privado B", html);
    }

    [Fact]
    public async Task Retained_order_card_binds_both_caption_counts_while_live_fragments_follow_pickup_and_C_delivery()
    {
        var first = await Create(app.TenantA, app.ShopA, app.CustomerA);
        var second = await Create(app.TenantA, app.ShopA, app.CustomerA);
        await Assign(first, app.PilotA);
        await Assign(second, app.PilotA);
        using var manager = await app.LoginWebAsync("manager-a");
        using var pilot = await Pilot();

        // Capture the original page once, just as an open dashboard keeps its
        // metric cards while receiving several later table fragments.
        var retainedPage = await manager.GetStringAsync("/Operaciones/Pedidos");
        AssertOrderCard(retainedPage, 2, 0);
        AssertSummary(retainedPage, 2, 0, 0);
        await AssertFragment(manager, 2, 0, 0);

        await Pickup(pilot, first);
        await AssertFragment(manager, 1, 1, 0);
        var monitoring = await manager.GetStringAsync("/Operaciones");
        Assert.Equal(1, BoundValue(monitoring, "enRuta"));
        AssertSummary(monitoring, 1, 1, 0);

        await Deliver(pilot, first);
        await AssertFragment(manager, 1, 0, 1);
        await Pickup(pilot, second);
        await AssertFragment(manager, 0, 1, 1);
        await Deliver(pilot, second);
        await AssertFragment(manager, 0, 0, 2);

        AssertOrderCard(await manager.GetStringAsync("/Operaciones/Pedidos"), 0, 0);
        Assert.Equal(0, BoundValue(await manager.GetStringAsync("/Operaciones"), "enRuta"));
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.OrdenesEntrega.CountAsync(order => order.Estado == EstadoOrden.Entregada));
            Assert.False(await db.RutaOrdenesOperativas.AnyAsync());
        });
    }

    [Fact]
    public async Task Rendered_and_live_caption_counts_share_store_pilot_and_company_scope()
    {
        var first = await Create(app.TenantA, app.ShopA, app.CustomerA);
        var secondShop = await Create(app.TenantA, app.ShopA2, app.CustomerA2);
        var foreign = await Create(app.TenantB, app.ShopB, app.CustomerB);
        await Assign(first, app.PilotA);
        await Assign(secondShop, app.PilotA2);
        await app.InScopeAsync(services => services.GetRequiredService<IOrdenService>()
            .AceptarOrdenAutoAsync(foreign.OrdenId, app.PilotB, "pilot-b", "Piloto"));
        using var secondPilot = await Pilot("pilot-a2");
        await Pickup(secondPilot, secondShop);
        using var manager = await app.LoginWebAsync("manager-a");

        foreach (var (filter, pickingUp, onTheWay) in new[]
        {
            ("", 1, 1),
            ($"&comercioId={app.ShopA}", 1, 0),
            ($"&comercioId={app.ShopA2}", 0, 1),
            ($"&repartidorId={app.PilotA}", 1, 0),
            ($"&repartidorId={app.PilotA2}", 0, 1),
            ($"&comercioId={app.ShopB}", 0, 0),
            ($"&repartidorId={app.PilotB}", 0, 0)
        })
        {
            var query = filter.Length == 0 ? "" : "?" + filter[1..];
            var page = await manager.GetStringAsync("/Operaciones/Pedidos" + query);
            AssertOrderCard(page, pickingUp, onTheWay);
            AssertSummary(page, pickingUp, onTheWay, 0);
            Assert.DoesNotContain($"data-order-id=\"{foreign.OrdenId}\"", page);
            Assert.DoesNotContain("Destino privado B", page);
            await AssertFragment(manager, pickingUp, onTheWay, 0, filter, foreign.OrdenId);
        }

        await app.InScopeAsync(async services =>
        {
            var row = await services.GetRequiredService<ApplicationDbContext>().OrdenesEntrega
                .AsNoTracking().SingleAsync(order => order.Id == foreign.OrdenId);
            Assert.Equal(app.TenantB, row.ClienteAppId);
            Assert.Equal(app.PilotB, row.PilotoId);
            Assert.Equal(EstadoOrden.Asignada, row.Estado);
        });
    }
}
