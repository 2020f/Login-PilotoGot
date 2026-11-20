using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Login.Tests;

public sealed class AuditBackendRegressionTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    private DateOnly Today => DateOnly.FromDateTime(app.Clock.GetUtcNow().UtcDateTime.AddHours(-4));
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value);
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    private async Task<OrdenCreadaResult> PickedAsync(int? shop = null, int? customer = null)
    {
        OrdenCreadaResult order = null!;
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrdenService>();
            order = await service.CrearOrdenAsync(app.TenantA, shop ?? app.ShopA, customer ?? app.CustomerA);
            await service.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            await service.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key());
        });
        return order;
    }

    private async Task<int> ReceivedCashAsync(int orderId, decimal amount)
    {
        var id = 0;
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var actor = await service.ManagerAsync("manager-a");
            var created = Ack(await service.RegistrarCobroAsync(actor, new(orderId, amount, Key(), Key())));
            id = created.GetProperty("id").GetInt32();
            var verified = Ack(await service.ConciliarCobroAsync(actor, id,
                new(amount, "Verificación QA", created.GetProperty("version").GetString()!, Key())));
            await service.RecibirEfectivoAsync(actor, id, new(amount, Key(), verified.GetProperty("version").GetString()!, Key()));
        });
        return id;
    }

    [Fact]
    public async Task Explicit_cash_selection_uses_only_selected_receipts_and_replays_without_consuming_others()
    {
        var order = await PickedAsync();
        var first = await ReceivedCashAsync(order.OrdenId, 100m);
        var second = await ReceivedCashAsync(order.OrdenId, 900m);
        using var manager = await app.LoginWebAsync("manager-a");
        var page = await manager.GetStringAsync("/Operaciones/Liquidaciones?tab=Comercios");
        var csrf = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(csrf.Success);
        manager.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(csrf.Groups[1].Value));
        manager.DefaultRequestHeaders.Add("X-CSRF-TOKEN", WebUtility.HtmlDecode(csrf.Groups[1].Value));
        var request = new GenerarLiquidacionRequest(Today, Today, Key(), TiendaId: app.ShopA, CobroIds: [first]);
        var response = await manager.PostAsJsonAsync("/api/operaciones/liquidaciones", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = result.GetProperty("id").GetInt32();
        Assert.Equal(100m, result.GetProperty("neto").GetDecimal());
        var replay = await manager.PostAsJsonAsync("/api/operaciones/liquidaciones", request);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var actor = await service.ManagerAsync("manager-a");
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(id, (await db.CobrosOperativos.AsNoTracking().SingleAsync(c => c.Id == first)).LiquidacionId);
            Assert.Null((await db.CobrosOperativos.AsNoTracking().SingleAsync(c => c.Id == second)).LiquidacionId);
            var remainder = Ack(await service.GenerarLiquidacionAsync(actor,
                new(Today, Today, Key(), TiendaId: app.ShopA, CobroIds: [])));
            Assert.Equal(900m, remainder.GetProperty("neto").GetDecimal());
            Assert.Equal(2, await db.LiquidacionesOperativas.CountAsync());
        });
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("nonpositive")]
    [InlineData("missing")]
    [InlineData("foreignTenant")]
    [InlineData("otherShop")]
    [InlineData("notReceived")]
    [InlineData("outsidePeriod")]
    [InlineData("alreadySettled")]
    public async Task An_invalid_explicit_cash_selection_is_rejected_atomically(string invalid)
    {
        var order = await PickedAsync();
        var first = await ReceivedCashAsync(order.OrdenId, 100m);
        var second = await ReceivedCashAsync(order.OrdenId, 900m);
        var selected = new List<int> { first, second };
        var existingSettlements = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var actor = await service.ManagerAsync("manager-a");
            if (invalid == "duplicate") selected = [first, first];
            else if (invalid == "nonpositive") selected = [first, 0];
            else if (invalid == "missing") selected = [first, int.MaxValue];
            else if (invalid == "alreadySettled")
            {
                await service.GenerarLiquidacionAsync(actor, new(Today, Today, Key(), TiendaId: app.ShopA, CobroIds: [second]));
                existingSettlements = 1;
            }
            else if (invalid is "foreignTenant" or "otherShop")
            {
                // An otherwise eligible receipt belonging to another context, not a
                // nonexistent identifier; fixture-only data never reaches a live DB.
                var foreign = invalid == "foreignTenant";
                var tenant = foreign ? app.TenantB : app.TenantA;
                var shop = foreign ? app.ShopB : app.ShopA2;
                var customer = foreign ? app.CustomerB : app.CustomerA2;
                var otherOrder = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(tenant, shop, customer);
                var cash = new CobroOperativo { ClienteAppId = tenant, OrdenEntregaId = otherOrder.OrdenId,
                    TiendaId = shop, PilotoId = foreign ? app.PilotB : app.PilotA,
                    Importe = 900m, ImporteVerificado = 900m, ImporteEntregadoEmpresa = 900m,
                    ConciliadoAt = app.Clock.GetUtcNow().UtcDateTime, EntregadoEmpresaAt = app.Clock.GetUtcNow().UtcDateTime,
                    Referencia = Key(), ReferenciaEntregaEmpresa = Key(), RecibidoEmpresaPor = "fixture" };
                db.CobrosOperativos.Add(cash); await db.SaveChangesAsync(); selected = [first, cash.Id];
            }
            else
            {
                var cash = await db.CobrosOperativos.SingleAsync(c => c.Id == second);
                if (invalid == "notReceived") { cash.EntregadoEmpresaAt = null; cash.ImporteEntregadoEmpresa = null; }
                if (invalid == "outsidePeriod") cash.EntregadoEmpresaAt = app.Clock.GetUtcNow().UtcDateTime.AddDays(-2);
                await db.SaveChangesAsync();
            }
            var key = Key();
            var rejected = await Assert.ThrowsAsync<RejectedOperationException>(() => service.GenerarLiquidacionAsync(actor,
                new(Today, Today, key, TiendaId: app.ShopA, CobroIds: selected)));
            Assert.Equal(invalid is "duplicate" or "nonpositive" ? 400 : 409, rejected.StatusCode);
            Assert.Equal(existingSettlements, await db.LiquidacionesOperativas.CountAsync());
            Assert.Null((await db.CobrosOperativos.AsNoTracking().SingleAsync(c => c.Id == first)).LiquidacionId);
            Assert.False(await db.OperacionesEnvirapido.AnyAsync(o => o.Tipo == "GenerarLiquidacion" && o.Clave == key));
        });
    }

    [Fact]
    public async Task Concurrent_settlements_cannot_consume_the_same_selected_receipt_twice()
    {
        var order = await PickedAsync();
        var cash = await ReceivedCashAsync(order.OrdenId, 100m);
        var ready = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Generate()
        {
            using var scope = app.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<EnvirapidoOperationsService>();
            var actor = await service.ManagerAsync("manager-a");
            if (Interlocked.Increment(ref ready) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(15));
            try { await service.GenerarLiquidacionAsync(actor, new(Today, Today, Key(), TiendaId: app.ShopA, CobroIds: [cash])); return true; }
            catch (RejectedOperationException ex) { Assert.Equal(409, ex.StatusCode); return false; }
        }
        var outcomes = await Task.WhenAll(Generate(), Generate());
        Assert.Single(outcomes.Where(x => x));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(100m, (await db.LiquidacionesOperativas.SingleAsync()).Neto);
            Assert.Equal(1, await db.MovimientosOperativos.CountAsync(m => m.Concepto == "CobrosComercio"));
        });
    }

    private static OrdenEntrega FixtureOrder(int tenant, int shop, int customer, int sequence, EstadoOrden state, DateTime at, string name)
    {
        var order = new OrdenEntrega { ClienteAppId = tenant, TiendaId = shop, UsuarioFinalId = customer,
            NumeroOrdenA = sequence, Estado = state, DatosEntregaCongelados = true, DestinoNombre = name,
            CreatedAt = at, EntregadaAt = state == EstadoOrden.Entregada ? at : null,
            FinalizadaAt = state == EstadoOrden.Entregada ? at : null };
        order.Paradas.Add(new() { ClienteAppId = tenant, TiendaId = shop, UsuarioFinalId = customer,
            Posicion = 1, Nombre = name, Estado = state == EstadoOrden.Entregada ? EstadoParadaEntrega.Entregada : EstadoParadaEntrega.Pendiente,
            EntregadaAt = order.EntregadaAt });
        return order;
    }

    [Fact]
    public async Task Store_lists_keep_every_active_order_and_history_is_paged_searchable_and_scoped()
    {
        var oldActive = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var now = app.Clock.GetUtcNow().UtcDateTime;
            for (var i = 0; i < 221; i++)
                db.OrdenesEntrega.Add(FixtureOrder(app.TenantA, app.ShopA, app.CustomerA, i + 1,
                    EstadoOrden.Creada, now.AddDays(-10).AddSeconds(i), "ACTIVE-VERY-OLD-" + i));
            for (var i = 0; i < 205; i++)
                db.OrdenesEntrega.Add(FixtureOrder(app.TenantA, app.ShopA, app.CustomerA, 10001 + i,
                    EstadoOrden.Entregada, now.AddDays(-1).AddSeconds(i), i == 0 ? "historyneedle" : "Terminal " + i));
            db.OrdenesEntrega.Add(FixtureOrder(app.TenantB, app.ShopB, app.CustomerB, 1, EstadoOrden.Entregada, now, "historyneedle"));
            await db.SaveChangesAsync();
            oldActive = await db.OrdenesEntrega.Where(o => o.ClienteAppId == app.TenantA && o.NumeroOrdenA == 1).Select(o => o.Id).SingleAsync();
            var referenced = await db.OrdenesEntrega.SingleAsync(o => o.ClienteAppId == app.TenantA && o.NumeroOrdenA == 10001);
            db.DatosEnviosOperativos.Add(new() { ClienteAppId = app.TenantA, OrdenEntregaId = referenced.Id, Referencia = "AUDIT-EXTERNAL-REF" });
            await db.SaveChangesAsync();
        });
        using var shop = await app.LoginAsync("shop-a");
        var list = (await shop.GetFromJsonAsync<List<OrdenTiendaDto>>("/api/tienda/ordenes"))!;
        Assert.Equal(221, list.Count(o => o.Estado == "Creada"));
        Assert.Equal(50, list.Count(o => o.Estado == "Entregada"));
        Assert.Contains(list, o => o.OrdenId == oldActive);
        var first = (await shop.GetFromJsonAsync<PaginaOrdenesTiendaDto>("/api/tienda/ordenes/historial?tamano=30"))!;
        var last = (await shop.GetFromJsonAsync<PaginaOrdenesTiendaDto>("/api/tienda/ordenes/historial?pagina=7&tamano=30"))!;
        Assert.Equal(205, first.Total); Assert.Equal(7, first.TotalPaginas); Assert.Equal(30, first.Items.Count);
        Assert.Equal(25, last.Items.Count); Assert.DoesNotContain(first.Items, o => last.Items.Any(p => p.OrdenId == o.OrdenId));
        foreach (var search in new[] { "historyneedle", "AUDIT-EXTERNAL-REF", "10001" })
        {
            var found = (await shop.GetFromJsonAsync<PaginaOrdenesTiendaDto>("/api/tienda/ordenes/historial?buscar=" + search))!;
            Assert.Equal(1, found.Total); Assert.Equal(10001, Assert.Single(found.Items).NumeroOrdenA);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await shop.GetAsync("/api/tienda/ordenes/historial?pagina=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await shop.GetAsync("/api/tienda/ordenes/historial?tamano=101")).StatusCode);
        using var web = await app.LoginWebAsync("shop-a");
        var html = await web.GetStringAsync("/Tienda/Ordenes?buscarHistorial=historyneedle");
        var activeSection = Regex.Match(html, "<section data-tp-orders=\"activas\">(.*?)</section>", RegexOptions.Singleline);
        var historySection = Regex.Match(html, "<section data-tp-orders=\"historial\">(.*?)</section>", RegexOptions.Singleline);
        Assert.True(activeSection.Success); Assert.True(historySection.Success);
        Assert.Equal(221, Regex.Matches(activeSection.Value, "data-order-id=").Count);
        Assert.Contains($"data-order-id=\"{oldActive}\"", activeSection.Value);
        Assert.Single(Regex.Matches(historySection.Value, "data-order-id="));
        Assert.Contains("historyneedle", historySection.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_orders_freeze_the_geocodes_used_by_pricing_once_and_replay_never_geocodes(bool multi)
    {
        OrdenCreadaResult legacy = null!;
        await app.InScopeAsync(async sp => legacy = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA));
        var maps = new GeocodingMaps();
        using var child = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IGoogleMapsService>(); services.AddSingleton<IGoogleMapsService>(maps); }));
        using var scope = child.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.ClientesApp.SingleAsync(t => t.Id == app.TenantA);
        tenant.PrecioBase = 10m; tenant.PrecioPorKm = 5m;
        var second = new UsuarioFinal { TiendaId = app.ShopA, Nombre = "Second", DireccionUbicacion = "Second address", Telefono = "5550199" };
        db.UsuariosFinales.Add(second); await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IOrdenService>();
        var key = Key();
        IReadOnlyList<OrdenDestinatarioRequest>? recipients = multi ? [new(app.CustomerA), new(second.Id)] : null;
        var created = await service.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: key, destinatarios: recipients);
        var frozen = await db.OrdenesEntrega.AsNoTracking().Include(o => o.Paradas).SingleAsync(o => o.Id == created.OrdenId);
        Assert.Equal(multi ? 3 : 2, maps.Geocodes.Count); Assert.All(maps.Geocodes.Values, count => Assert.Equal(1, count));
        Assert.Equal(18.4, frozen.RecogidaLatitud); Assert.Equal(-69.9, frozen.RecogidaLongitud);
        var first = frozen.Paradas.Single(p => p.UsuarioFinalId == app.CustomerA);
        Assert.Equal(18.5, first.Latitud); Assert.Equal(-69.8, first.Longitud);
        if (!multi)
        {
            Assert.Equal(first.Latitud, frozen.DestinoLatitud); Assert.Equal(first.Longitud, frozen.DestinoLongitud);
            Assert.Equal(20m, frozen.Precio); Assert.Equal(2, frozen.DistanciaKm);
            Assert.Equal((18.4, -69.9, 18.5, -69.8), Assert.Single(maps.RouteInputs));
        }
        else { Assert.Equal(18.6, frozen.Paradas.Single(p => p.UsuarioFinalId == second.Id).Latitud); Assert.Empty(maps.RouteInputs); }
        // A later contact edit must neither alter frozen history nor force Maps on replay.
        var contact = await db.UsuariosFinales.SingleAsync(u => u.Id == app.CustomerA);
        contact.DireccionUbicacion = "Changed afterwards"; contact.Latitud = 19; contact.Longitud = -70;
        await db.SaveChangesAsync();
        Assert.Equal(created, await service.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: key, destinatarios: recipients));
        Assert.Equal(multi ? 3 : 2, maps.Geocodes.Count);
        Assert.Null((await db.OrdenesEntrega.AsNoTracking().SingleAsync(o => o.Id == legacy.OrdenId)).DestinoLatitud);
        Assert.Equal(18.5, (await db.ParadasEntrega.AsNoTracking().SingleAsync(p => p.Id == first.Id)).Latitud);
        Assert.Null((await db.Tiendas.AsNoTracking().SingleAsync(t => t.Id == app.ShopA)).Latitud);
    }

    [Fact]
    public async Task Unresolved_geocodes_remain_null_without_fabricating_a_route_or_coordinates()
    {
        var maps = new GeocodingMaps { Resolve = false };
        using var child = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IGoogleMapsService>(); services.AddSingleton<IGoogleMapsService>(maps); }));
        using var scope = child.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.ClientesApp.SingleAsync(t => t.Id == app.TenantA);
        tenant.PrecioBase = 10m; tenant.PrecioPorKm = 5m; await db.SaveChangesAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA);
        var order = await db.OrdenesEntrega.AsNoTracking().Include(o => o.Paradas).SingleAsync(o => o.Id == result.OrdenId);
        Assert.Null(order.RecogidaLatitud); Assert.Null(order.DestinoLatitud); Assert.Null(order.Paradas.Single().Latitud);
        Assert.Null(order.DistanciaKm); Assert.Equal(10m, order.Precio); Assert.Empty(maps.RouteInputs);
        Assert.Equal(2, maps.Geocodes.Count); Assert.All(maps.Geocodes.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task Recipient_used_only_by_a_multi_stop_order_is_rejected_in_API_and_both_web_delete_actions()
    {
        var recipient = 0; var unused = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var used = new UsuarioFinal { TiendaId = app.ShopA, Nombre = "Used in stop", DireccionUbicacion = "Fake address", Telefono = "5550199" };
            var free = new UsuarioFinal { TiendaId = app.ShopA, Nombre = "Unused", DireccionUbicacion = "Fake address", Telefono = "5550198" };
            db.AddRange(used, free); await db.SaveChangesAsync(); recipient = used.Id; unused = free.Id;
            await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA,
                destinatarios: [new(app.CustomerA), new(recipient)]);
            Assert.False(await db.OrdenesEntrega.AnyAsync(o => o.UsuarioFinalId == recipient));
        });
        using var shop = await app.LoginAsync("shop-a");
        var response = await shop.DeleteAsync($"/api/tienda/usuarios-finales/{recipient}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ya tiene órdenes", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        using var web = await app.LoginWebAsync("shop-a");
        Assert.Equal(HttpStatusCode.Redirect, (await web.GetAsync($"/Tienda/EliminarUsuarioFinal/{recipient}")).StatusCode);
        var html = await web.GetStringAsync("/Tienda/UsuariosFinales");
        var csrf = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(csrf.Success);
        var post = await web.PostAsync("/Tienda/EliminarUsuarioFinalConfirmado", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["id"] = recipient.ToString(), ["__RequestVerificationToken"] = WebUtility.HtmlDecode(csrf.Groups[1].Value) }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("No puedes eliminar", Uri.UnescapeDataString(post.Headers.Location!.OriginalString));
        Assert.Equal(HttpStatusCode.OK, (await shop.DeleteAsync($"/api/tienda/usuarios-finales/{unused}")).StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.True(await db.UsuariosFinales.AnyAsync(u => u.Id == recipient));
            Assert.True(await db.ParadasEntrega.AnyAsync(p => p.UsuarioFinalId == recipient));
            Assert.False(await db.UsuariosFinales.AnyAsync(u => u.Id == unused));
        });
    }

    private sealed class GeocodingMaps : IGoogleMapsService
    {
        public bool Resolve { get; init; } = true;
        public Dictionary<string, int> Geocodes { get; } = new();
        public List<(double, double, double, double)> RouteInputs { get; } = new();
        public string ApiKey => "";
        public string BrowserApiKey => "";
        public Task<(double Lat, double Lng)?> GeocodificarAsync(string address)
        {
            Geocodes[address] = Geocodes.GetValueOrDefault(address) + 1;
            return Task.FromResult<(double, double)?>(!Resolve ? null : address switch
            { "Calle A" => (18.4, -69.9), "Destino privado A" => (18.5, -69.8), "Second address" => (18.6, -69.7), _ => null });
        }
        public Task<double?> DistanciaRutaKmAsync(double a, double b, double c, double d)
        { RouteInputs.Add((a, b, c, d)); return Task.FromResult<double?>(2); }
        public double CalcularDistanciaKm(double a, double b, double c, double d) => throw new InvalidOperationException("Routes succeeded; no fallback expected.");
    }
}
