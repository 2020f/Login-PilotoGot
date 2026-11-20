using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ConciliationCashIndicatorsTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.GetUtcNow(), Zone).DateTime);
    private static DateTime At(DateOnly day, int hour = 12)
        => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Zone);
    private static JsonElement Ack(object value)
        => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    // These fixtures represent already collected deliveries. Cash operations,
    // not the independently covered pickup workflow, are the subject here.
    private async Task<int> SourceOrder(int tenant, int shop, int customer, int pilot)
    {
        var id = 0;
        await app.InScopeAsync(async services =>
        {
            var core = services.GetRequiredService<IOrdenService>();
            var created = await core.CrearOrdenAsync(tenant, shop, customer, operacionId: Key());
            id = created.OrdenId;
            var db = services.GetRequiredService<ApplicationDbContext>();
            var order = (await db.OrdenesEntrega.FindAsync(id))!;
            order.PilotoId = pilot; order.Estado = EstadoOrden.Recolectada;
            order.AssignedAt = app.Clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
            order.RecolectadaAt = app.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        return id;
    }

    private CobroOperativo Cash(int order, int tenant, int shop, int pilot, decimal declared,
        decimal? verified = null, bool received = false, DateTime? created = null)
        => new()
        {
            ClienteAppId = tenant, OrdenEntregaId = order, TiendaId = shop, PilotoId = pilot,
            Referencia = Key(), Importe = declared, ImporteVerificado = verified,
            ConciliadoAt = verified.HasValue ? app.Clock.GetUtcNow().UtcDateTime : null,
            EntregadoEmpresaAt = received ? app.Clock.GetUtcNow().UtcDateTime : null,
            ImporteEntregadoEmpresa = received ? verified : null,
            CreatedAt = created ?? At(Today)
        };

    private static void AssertMetric(string html, string key, decimal amount)
    {
        var card = Regex.Match(html, $"<article[^>]*data-ev-cash-metric=\"{key}\"[^>]*>(.*?)</article>", RegexOptions.Singleline);
        Assert.True(card.Success, $"Falta la tarjeta de cobros {key}.");
        var money = Regex.Match(card.Groups[1].Value, "<strong[^>]*>(.*?)</strong>", RegexOptions.Singleline);
        Assert.Equal("RD$ " + amount.ToString("N2", CultureInfo.CurrentCulture), WebUtility.HtmlDecode(money.Groups[1].Value));
    }

    [Fact]
    public async Task Empty_cash_period_is_zero_without_reusing_existing_settlement_totals()
    {
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.LiquidacionesOperativas.Add(new() { ClienteAppId = app.TenantA, PilotoId = app.PilotA,
                Desde = Today, Hasta = Today, Neto = 87.65m, Estado = EstadoLiquidacion.Pagada });
            await db.SaveChangesAsync();
            var ops = services.GetRequiredService<EnvirapidoOperationsService>();
            var result = await ops.IndicadoresAsync(await ops.ManagerAsync("manager-a"), Today, Today);
            Assert.Equal(new ResumenCobrosPeriodo(0, 0m, 0, 0m, 0, 0m, 0, 0m, 0, 0m), result.CobrosPeriodo);
            Assert.Equal(87.65m, result.NetoPeriodo);
            Assert.Equal(1, result.LiquidacionesPorEstado["Pagada"].Cantidad);
        });
    }

    [Fact]
    public async Task Register_verify_and_receive_200_updates_indicators_and_refreshed_fragment_without_creating_settlement()
    {
        var order = await SourceOrder(app.TenantA, app.ShopA, app.CustomerA, app.PilotA);
        var cashId = 0; var version = "";
        await app.InScopeAsync(async services =>
        {
            var ops = services.GetRequiredService<EnvirapidoOperationsService>();
            var actor = await ops.ManagerAsync("manager-a");
            var cash = Ack(await ops.RegistrarCobroAsync(actor, new(order, 200m, Key(), Key())));
            cashId = cash.GetProperty("id").GetInt32(); version = cash.GetProperty("version").GetString()!;
            var summary = (await ops.IndicadoresAsync(actor)).CobrosPeriodo;
            Assert.Equal(new ResumenCobrosPeriodo(1, 200m, 0, 0m, 0, 0m, 0, 0m, 0, 0m), summary);
        });
        using var manager = await app.LoginWebAsync("manager-a");
        var fragmentPath = "/Operaciones/ModuloFragmento?seccion=Liquidaciones&tab=Conciliaci%C3%B3n";
        var declaredPage = await manager.GetStringAsync(fragmentPath);
        AssertMetric(declaredPage, "registered", 200m); AssertMetric(declaredPage, "received", 0m);
        await app.InScopeAsync(async services =>
        {
            var ops = services.GetRequiredService<EnvirapidoOperationsService>();
            var actor = await ops.ManagerAsync("manager-a");
            var verified = Ack(await ops.ConciliarCobroAsync(actor, cashId, new(200m, "Importe comprobado QA", version, Key())));
            version = verified.GetProperty("version").GetString()!;
            Assert.Equal(new ResumenCobrosPeriodo(1, 200m, 1, 200m, 0, 0m, 1, 200m, 0, 0m),
                (await ops.IndicadoresAsync(actor)).CobrosPeriodo);
        });
        var verifiedPage = await manager.GetStringAsync(fragmentPath);
        AssertMetric(verifiedPage, "verified", 200m); AssertMetric(verifiedPage, "pending", 200m);
        await app.InScopeAsync(async services =>
        {
            var ops = services.GetRequiredService<EnvirapidoOperationsService>();
            var actor = await ops.ManagerAsync("manager-a");
            await ops.RecibirEfectivoAsync(actor, cashId, new(200m, Key(), version, Key()));
            var snapshot = await ops.SnapshotAsync(actor);
            Assert.Equal(new ResumenCobrosPeriodo(1, 200m, 1, 200m, 1, 200m, 0, 0m, 0, 0m), snapshot.Indicadores!.CobrosPeriodo);
            Assert.Empty(snapshot.Liquidaciones);
            Assert.Equal(0m, snapshot.Indicadores.NetoPeriodo);
            Assert.Equal(0, await services.GetRequiredService<ApplicationDbContext>().LiquidacionesOperativas.CountAsync());
        });
        var receivedPage = await manager.GetStringAsync(fragmentPath);
        AssertMetric(receivedPage, "registered", 200m); AssertMetric(receivedPage, "verified", 200m);
        AssertMetric(receivedPage, "received", 200m); AssertMetric(receivedPage, "pending", 0m);
        AssertMetric(receivedPage, "differences", 0m);
        Assert.DoesNotContain("Neto del período", WebUtility.HtmlDecode(receivedPage));
    }

    [Fact]
    public async Task Unverified_cash_is_not_pending_verified_receipt_and_opposite_differences_never_cancel_out()
    {
        var order = await SourceOrder(app.TenantA, app.ShopA, app.CustomerA, app.PilotA);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.CobrosOperativos.AddRange(Cash(order, app.TenantA, app.ShopA, app.PilotA, 40m),
                Cash(order, app.TenantA, app.ShopA, app.PilotA, 100m, 90m),
                Cash(order, app.TenantA, app.ShopA, app.PilotA, 100m, 110m),
                Cash(order, app.TenantA, app.ShopA, app.PilotA, 50m, 50m, received: true));
            await db.SaveChangesAsync();
            var ops = services.GetRequiredService<EnvirapidoOperationsService>();
            var summary = (await ops.IndicadoresAsync(await ops.ManagerAsync("manager-a"))).CobrosPeriodo;
            Assert.Equal(new ResumenCobrosPeriodo(4, 290m, 3, 250m, 1, 50m, 2, 200m, 2, 20m), summary);
        });
    }

    [Fact]
    public async Task Cash_totals_remain_scoped_to_each_company_and_each_pilots_own_deliveries()
    {
        var first = await SourceOrder(app.TenantA, app.ShopA, app.CustomerA, app.PilotA);
        var second = await SourceOrder(app.TenantA, app.ShopA2, app.CustomerA2, app.PilotA2);
        var foreign = await SourceOrder(app.TenantB, app.ShopB, app.CustomerB, app.PilotB);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.CobrosOperativos.AddRange(Cash(first, app.TenantA, app.ShopA, app.PilotA, 100m, 100m, true),
                Cash(second, app.TenantA, app.ShopA2, app.PilotA2, 200m, 200m, true),
                Cash(foreign, app.TenantB, app.ShopB, app.PilotB, 9000m, 9000m, true));
            await db.SaveChangesAsync();
            var ops = services.GetRequiredService<EnvirapidoOperationsService>();
            Assert.Equal(300m, (await ops.IndicadoresAsync(await ops.ManagerAsync("manager-a"))).CobrosPeriodo!.ImporteRecibidoEmpresa);
            Assert.Equal(100m, (await ops.IndicadoresAsync(await ops.PilotAsync("pilot-a"))).CobrosPeriodo!.ImporteRecibidoEmpresa);
            Assert.Equal(200m, (await ops.IndicadoresAsync(await ops.PilotAsync("pilot-a2"))).CobrosPeriodo!.ImporteRecibidoEmpresa);
            Assert.Equal(9000m, (await ops.IndicadoresAsync(await ops.PilotAsync("pilot-b"))).CobrosPeriodo!.ImporteRecibidoEmpresa);
            Assert.Equal(403, (await Assert.ThrowsAsync<OperationsException>(() =>
                ops.IndicadoresAsync(new(app.TenantB, "manager-a")))).StatusCode);
        });
    }

    [Fact]
    public async Task Selected_period_uses_cash_registration_midnight_in_RD_not_UTC_or_receipt_date()
    {
        var order = await SourceOrder(app.TenantA, app.ShopA, app.CustomerA, app.PilotA);
        var start = At(Today, 0); var end = At(Today.AddDays(1), 0);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.CobrosOperativos.AddRange(
                Cash(order, app.TenantA, app.ShopA, app.PilotA, 10m, 10m, true, start.AddTicks(-1)),
                Cash(order, app.TenantA, app.ShopA, app.PilotA, 1.11m, 1.11m, true, start),
                Cash(order, app.TenantA, app.ShopA, app.PilotA, 2.22m, 2.22m, true, end.AddTicks(-1)),
                Cash(order, app.TenantA, app.ShopA, app.PilotA, 100m, 100m, true, end));
            await db.SaveChangesAsync();
            var ops = services.GetRequiredService<EnvirapidoOperationsService>(); var actor = await ops.ManagerAsync("manager-a");
            var snapshot = await ops.SnapshotAsync(actor, Today, Today);
            Assert.Equal(2, snapshot.Cobros.Count);
            Assert.Equal(new ResumenCobrosPeriodo(2, 3.33m, 2, 3.33m, 2, 3.33m, 0, 0m, 0, 0m), snapshot.Indicadores!.CobrosPeriodo);
            var yesterday = (await ops.IndicadoresAsync(actor, Today.AddDays(-1), Today.AddDays(-1))).CobrosPeriodo!;
            Assert.Equal(10m, yesterday.ImporteRecibidoEmpresa);
            Assert.Equal(1, yesterday.Cantidad);
            Assert.Equal(113.33m, (await ops.IndicadoresAsync(actor)).CobrosPeriodo!.ImporteRegistrado);
        });
    }

    [Fact]
    public async Task Financial_cash_summary_includes_rows_beyond_snapshot_table_cap_with_exact_decimal_precision()
    {
        var order = await SourceOrder(app.TenantA, app.ShopA, app.CustomerA, app.PilotA);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.CobrosOperativos.AddRange(Enumerable.Range(0, 2005).Select(_ =>
                Cash(order, app.TenantA, app.ShopA, app.PilotA, .01m, .01m, true)));
            await db.SaveChangesAsync();
            var ops = services.GetRequiredService<EnvirapidoOperationsService>();
            var snapshot = await ops.SnapshotAsync(await ops.ManagerAsync("manager-a"));
            Assert.Equal(2000, snapshot.Cobros.Count);
            Assert.Equal(2005, snapshot.Cobertura!.TotalCobros);
            Assert.Equal(new ResumenCobrosPeriodo(2005, 20.05m, 2005, 20.05m, 2005, 20.05m, 0, 0m, 0, 0m), snapshot.Indicadores!.CobrosPeriodo);
        });
    }

    [Fact]
    public async Task Conciliation_page_uses_cash_cards_while_other_tabs_keep_settlement_cards_and_the_same_period()
    {
        var order = await SourceOrder(app.TenantA, app.ShopA, app.CustomerA, app.PilotA);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.CobrosOperativos.AddRange(Cash(order, app.TenantA, app.ShopA, app.PilotA, 200m, 200m, true),
                Cash(order, app.TenantA, app.ShopA, app.PilotA, 999m, 999m, true, At(Today.AddDays(-1))));
            db.LiquidacionesOperativas.AddRange(
                new LiquidacionOperativa { ClienteAppId = app.TenantA, PilotoId = app.PilotA, Desde = Today, Hasta = Today,
                    Neto = 75m, Estado = EstadoLiquidacion.EnRevision },
                new LiquidacionOperativa { ClienteAppId = app.TenantA, TiendaId = app.ShopA, Desde = Today, Hasta = Today,
                    Neto = 25m, Estado = EstadoLiquidacion.EnRevision });
            await db.SaveChangesAsync();
        });
        using var manager = await app.LoginWebAsync("manager-a");
        var period = $"&desde={Today:yyyy-MM-dd}&hasta={Today:yyyy-MM-dd}";
        var html = await manager.GetStringAsync("/Operaciones/Liquidaciones?tab=Conciliaci%C3%B3n" + period);
        AssertMetric(html, "registered", 200m); AssertMetric(html, "received", 200m);
        Assert.DoesNotContain("Neto del período", WebUtility.HtmlDecode(html));
        foreach (var tab in new[] { "Repartidores", "Comercios", "Historial" })
        {
            var other = WebUtility.HtmlDecode(await manager.GetStringAsync("/Operaciones/Liquidaciones?tab=" + tab + period));
            Assert.Contains("Neto del período", other);
            Assert.DoesNotContain("data-ev-cash-metric", other);
            Assert.Contains("RD$ " + (tab == "Repartidores" ? 75m : tab == "Comercios" ? 25m : 100m).ToString("N2", CultureInfo.CurrentCulture), other);
        }
    }
}
