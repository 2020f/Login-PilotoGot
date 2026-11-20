using System.Net;
using System.Text;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Internal;

namespace Login.Tests;

public sealed class RecorridosTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public List<JsonElement> Requests = []; public List<string> Masks = [];
        public int MissingLegData;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? Release;
        public void Reset() { Interlocked.Exchange(ref Calls, 0); lock (Requests) { Requests.Clear(); Masks.Clear(); } }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref Calls);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = document.RootElement.Clone();
            lock (Requests) { Requests.Add(body); Masks.Add(string.Join(',', request.Headers.GetValues("X-Goog-FieldMask"))); }
            Started.TrySetResult(true);
            if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
            else await Task.Delay(10, cancellationToken);
            var count = body.GetProperty("intermediates").GetArrayLength() + 1;
            var legs = Enumerable.Range(0, count).Select(i => new Dictionary<string, object> {
                ["distanceMeters"] = 2400d / count, ["duration"] = (480d / count).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s",
                ["polyline"] = new { encodedPolyline = $"provider-leg-{call}-{i}" } }).ToList();
            if (MissingLegData == 2) legs.RemoveAt(legs.Count - 1);
            if (MissingLegData == 3) legs[0].Remove("duration");
            var route = new Dictionary<string, object> { ["distanceMeters"] = 2400, ["duration"] = "480s", ["polyline"] = new { encodedPolyline = "provider-path" } };
            if (MissingLegData != 1) route["legs"] = legs;
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { routes = new[] { route } }), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Maps(string key = "fake-provider-key") : IGoogleMapsService
    {
        public string ApiKey => key; public string BrowserApiKey => "";
        public Task<(double Lat, double Lng)?> GeocodificarAsync(string address) => Task.FromResult<(double, double)?>(null);
        public Task<double?> DistanciaRutaKmAsync(double a, double b, double c, double d) => Task.FromResult<double?>(null);
        public double CalcularDistanciaKm(double a, double b, double c, double d) => 0;
    }
    private sealed class Clock : TimeProvider, ISystemClock
    {
        private long ticks = DateTimeOffset.UtcNow.UtcTicks;
        public DateTimeOffset UtcNow => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan interval) => Interlocked.Add(ref ticks, interval.Ticks);
    }
    private static List<PuntoRutaDto> Points(int count) => Enumerable.Range(1, count).Select(i => new PuntoRutaDto(i, i, i, "Entrega", i, "Parada", "Dirección QA", 18.48 + i * .0001, -69.91, false, true, null)).ToList();

    [Fact]
    public async Task Provider_route_is_cached_for_concurrent_polls_and_uses_the_selected_sequence()
    {
        var handler = new Handler(); using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new RecorridosService(http, new Maps(), cache, TimeProvider.System, new ConfigurationBuilder().Build());
        var points = Points(3); var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.EstimarAsync(1, "v1", 18.48, -69.91, DateTime.UtcNow, points)));
        Assert.Equal(1, handler.Calls); Assert.All(results, r => { Assert.Equal(2.4, r!.DistanciaKm); Assert.Equal(8, r.Minutos); Assert.Contains("provider-path", r.Geometria); Assert.Equal(3, r.Tramos!.Count); });
        Assert.Equal(2, Assert.Single(handler.Requests).GetProperty("intermediates").GetArrayLength());
        Assert.Contains("routes.legs.duration", Assert.Single(handler.Masks));
        Assert.False(handler.Requests[0].GetProperty("optimizeWaypointOrder").GetBoolean());
        await service.EstimarAsync(1, "v2", 18.48, -69.91, DateTime.UtcNow, points); Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Old_private_or_missing_positions_do_not_trigger_a_provider_request_and_long_routes_are_bounded()
    {
        var handler = new Handler(); using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Envirapido:GpsFrescuraSegundos"] = "60" }).Build();
        var service = new RecorridosService(http, new Maps(), cache, TimeProvider.System, config);
        Assert.Null(await service.EstimarAsync(2, "v1", 18.48, -69.91, DateTime.UtcNow.AddSeconds(-90), Points(3)));
        Assert.Null(await service.EstimarAsync(2, "v1", 18.48, -69.91, DateTime.UtcNow, [Points(1)[0] with { Latitud = null }]));
        Assert.Null(await service.EstimarAsync(2, "v1", 18.48, -69.91, DateTime.UtcNow, [Points(1)[0] with { PuedeVerDestino = false }]));
        Assert.Null(await service.EstimarAsync(2, "v1", double.NaN, -69.91, DateTime.UtcNow, Points(1)));
        Assert.Null(await new RecorridosService(http, new Maps(""), cache, TimeProvider.System, config).EstimarAsync(2, "v1", 18.48, -69.91, DateTime.UtcNow, Points(1)));
        Assert.Equal(0, handler.Calls);
        var result = await service.EstimarAsync(3, "v1", 18.48, -69.91, DateTime.UtcNow, Points(45));
        Assert.Equal(3, handler.Calls); Assert.Equal(7.2, result!.DistanciaKm); Assert.Equal(45, result.Tramos!.Count);
        Assert.All(handler.Requests, r => Assert.InRange(r.GetProperty("intermediates").GetArrayLength(), 0, 19));
        var last = result.HastaPunto(45); Assert.Equal(result.DistanciaKm, last!.DistanciaKm); Assert.Equal(result.Minutos, last.Minutos);
    }

    [Fact]
    public async Task Twenty_targets_share_one_full_route_and_each_prefix_excludes_future_legs_then_progress_invalidates_cache()
    {
        var handler = new Handler(); using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new RecorridosService(http, new Maps(), cache, TimeProvider.System, new ConfigurationBuilder().Build()); var points = Points(20);
        var results = await Task.WhenAll(Enumerable.Range(1, 20).Select(async target => (await service.EstimarAsync(4, "v1", 18.48, -69.91, DateTime.UtcNow, points))!.HastaPunto(target)));
        Assert.Equal(1, handler.Calls);
        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(Math.Round(.12 * (i + 1), 2), results[i]!.DistanciaKm); Assert.Equal(Math.Round(.4 * (i + 1), 1), results[i]!.Minutos);
            var prefix = JsonSerializer.Deserialize<string[]>(results[i]!.Geometria)!; Assert.Equal(i + 1, prefix.Length);
            Assert.EndsWith($"-{i}", prefix[^1]); Assert.Equal(i + 1, results[i]!.Tramos!.Count);
        }
        var remaining = points.Skip(1).ToList();
        var progressed = await service.EstimarAsync(4, "v1", 18.48, -69.91, DateTime.UtcNow, remaining);
        Assert.Equal(2, handler.Calls); Assert.Null(progressed!.HastaPunto(1));
        (remaining[2], remaining[3]) = (remaining[3], remaining[2]);
        await service.EstimarAsync(4, "v1", 18.48, -69.91, DateTime.UtcNow, remaining);
        Assert.Equal(3, handler.Calls); // Mismo primero/último y longitud: también invalida un cambio intermedio.
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Missing_or_incomplete_provider_legs_never_invent_a_target_estimate(int missing)
    {
        var handler = new Handler { MissingLegData = missing }; using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new RecorridosService(http, new Maps(), cache, TimeProvider.System, new ConfigurationBuilder().Build());
        Assert.Null(await service.EstimarAsync(5, "v1", 18.48, -69.91, DateTime.UtcNow, Points(3)));
        Assert.Null(await service.EstimarAsync(5, "v1", 18.48, -69.91, DateTime.UtcNow, Points(3)));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Cancelling_one_waiter_preserves_the_shared_provider_computation_for_other_rows()
    {
        var handler = new Handler { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions()); using var cancelled = new CancellationTokenSource();
        var service = new RecorridosService(http, new Maps(), cache, TimeProvider.System, new ConfigurationBuilder().Build());
        var first = service.EstimarAsync(6, "v1", 18.48, -69.91, DateTime.UtcNow, Points(3), cancelled.Token);
        await handler.Started.Task; var survivor = service.EstimarAsync(6, "v1", 18.48, -69.91, DateTime.UtcNow, Points(3));
        cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.Release.SetResult(true); Assert.NotNull(await survivor); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Inflight_route_does_not_expire_mid_request_and_cached_old_GPS_cannot_be_revived_by_a_new_sample()
    {
        var handler = new Handler { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var clock = new Clock();
        using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var service = new RecorridosService(http, new Maps(), cache, clock, new ConfigurationBuilder().Build());
        var startedAt = clock.UtcNow.UtcDateTime;
        var first = service.EstimarAsync(7, "v1", 18.48, -69.91, startedAt, Points(3)); await handler.Started.Task;
        clock.Advance(TimeSpan.FromSeconds(70)); var second = service.EstimarAsync(7, "v1", 18.48, -69.91, clock.UtcNow.UtcDateTime, Points(3));
        Assert.Equal(1, handler.Calls); handler.Release.SetResult(true); Assert.NotNull(await first); Assert.NotNull(await second);
        clock.Advance(TimeSpan.FromSeconds(30)); Assert.NotNull(await service.EstimarAsync(7, "v1", 18.48, -69.91, clock.UtcNow.UtcDateTime, Points(3)));
        Assert.Equal(1, handler.Calls);
        clock.Advance(TimeSpan.FromSeconds(29)); Assert.Null(await service.EstimarAsync(7, "v1", 18.48, -69.91, clock.UtcNow.UtcDateTime, Points(3)));
        Assert.Equal(1, handler.Calls); // Se conserva el TTL, pero no se presenta como vigente una ruta calculada con GPS viejo.
        clock.Advance(TimeSpan.FromSeconds(2)); Assert.NotNull(await service.EstimarAsync(7, "v1", 18.48, -69.91, clock.UtcNow.UtcDateTime, Points(3)));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Default_order_ETA_follows_authorized_route_sequence_while_explicit_stop_keeps_its_own_prefix()
    {
        using var app = new PilotGoFactory(); await app.SeedAsync(); OrdenCreadaResult order = null!;
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>(); var store = (await db.Tiendas.FindAsync(app.ShopA))!;
            store.Latitud = 18.48; store.Longitud = -69.91;
            var customer = (await db.UsuariosFinales.FindAsync(app.CustomerA))!; customer.Latitud = 18.49; customer.Longitud = -69.92;
            var pilot = (await db.Pilotos.FindAsync(app.PilotA))!; pilot.Latitud = 18.485; pilot.Longitud = -69.915; pilot.UltimaUbicacionAt = app.Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(); var core = sp.GetRequiredService<IOrdenService>();
            order = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Guid.NewGuid().ToString("N"),
                destinatarios: Enumerable.Range(0, 3).Select(_ => new OrdenDestinatarioRequest(app.CustomerA)).ToArray());
            var ops = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await ops.ManagerAsync("manager-a");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")).DateTime);
            var route = JsonSerializer.SerializeToElement(await ops.CrearRutaAsync(manager, new("C reordenadas QA", today, [order.OrdenId], app.PilotA, null, false, Guid.NewGuid().ToString("N"))));
            var id = route.GetProperty("id").GetInt32(); var points = await db.PuntosRutaOperativos.Where(p => p.RutaId == id).OrderBy(p => p.Posicion).Select(p => p.Id).ToListAsync();
            (points[1], points[2]) = (points[2], points[1]);
            var moved = JsonSerializer.SerializeToElement(await ops.ReordenarRutaAsync(manager, id, new(points, route.GetProperty("version").GetString()!, Guid.NewGuid().ToString("N"))));
            await ops.EstadoRutaAsync(manager, id, new("Iniciar", moved.GetProperty("version").GetString()!, Guid.NewGuid().ToString("N")));
            await core.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Guid.NewGuid().ToString("N"));
        });
        var handler = new Handler(); using var http = new HttpClient(handler);
        using var visual = app.WithWebHostBuilder(web => web.ConfigureTestServices(services => {
            services.RemoveAll<RecorridosService>(); services.AddScoped(sp => new RecorridosService(http, new Maps(),
                sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IConfiguration>()));
        }));
        using var scope = visual.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<EnvirapidoOperationsService>();
        var managerActor = await service.ManagerAsync("manager-a"); var defaultEstimate = await service.SeguimientoPedidoAsync(managerActor, order.OrdenId);
        var explicitEstimate = await service.SeguimientoPedidoAsync(managerActor, order.OrdenId, order.Paradas[0].ParadaId);
        Assert.Single(defaultEstimate!.Tramos!); Assert.Equal(2, explicitEstimate!.Tramos!.Count);
        Assert.Equal(2.7, defaultEstimate.Minutos); Assert.Equal(5.3, explicitEstimate.Minutos); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Real_batch_of_twenty_orders_shares_route_with_single_and_snapshot_and_changes_after_delivery()
    {
        using var app = new PilotGoFactory(); await app.SeedAsync(); var orders = new List<OrdenCreadaResult>();
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>(); var store = (await db.Tiendas.FindAsync(app.ShopA))!;
            store.Latitud = 18.48; store.Longitud = -69.91;
            var customer = (await db.UsuariosFinales.FindAsync(app.CustomerA))!; customer.Latitud = 18.49; customer.Longitud = -69.92;
            var pilot = (await db.Pilotos.FindAsync(app.PilotA))!; pilot.Latitud = 18.485; pilot.Longitud = -69.915; pilot.UltimaUbicacionAt = app.Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(); var core = sp.GetRequiredService<IOrdenService>();
            for (var i = 0; i < 20; i++) orders.Add(await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Guid.NewGuid().ToString("N")));
            var ops = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await ops.ManagerAsync("manager-a");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")).DateTime);
            var route = JsonSerializer.SerializeToElement(await ops.CrearRutaAsync(manager, new("Veinte pedidos QA", today, orders.Select(o => o.OrdenId).ToList(), app.PilotA, null, false, Guid.NewGuid().ToString("N"))));
            await ops.EstadoRutaAsync(manager, route.GetProperty("id").GetInt32(), new("Iniciar", route.GetProperty("version").GetString()!, Guid.NewGuid().ToString("N")));
        });
        var handler = new Handler(); using var http = new HttpClient(handler);
        using var visual = app.WithWebHostBuilder(web => web.ConfigureTestServices(services => {
            services.RemoveAll<RecorridosService>(); services.AddScoped(sp => new RecorridosService(http, new Maps(),
                sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IConfiguration>()));
        }));
        using var scope = visual.Services.CreateScope(); var provider = scope.ServiceProvider;
        var service = provider.GetRequiredService<EnvirapidoOperationsService>(); var managerActor = await service.ManagerAsync("manager-a");
        Assert.NotNull(await service.SeguimientoPedidoAsync(managerActor, orders[0].OrdenId));
        var managerCalls = handler.Calls;
        Assert.Null(await service.SeguimientoPedidoAsync(await service.PilotAsync("pilot-a"), orders[0].OrdenId));
        Assert.Equal(managerCalls, handler.Calls); // El caché de CRM no revela destinos antes de B.
        var coreService = provider.GetRequiredService<IOrdenService>();
        await coreService.ConfirmarRecolectaAsync(orders[0].CodigoB, "pilot-a", "Piloto", orders[0].OrdenId, Guid.NewGuid().ToString("N"));
        Assert.Null(await service.SeguimientoPedidoAsync(await service.PilotAsync("pilot-a"), orders[0].OrdenId));
        Assert.Equal(managerCalls, handler.Calls); // Tampoco revela sufijos de otras órdenes que aún no se recogieron.
        foreach (var order in orders.Skip(1)) await coreService.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Guid.NewGuid().ToString("N"));
        var context = provider.GetRequiredService<ApplicationDbContext>(); var position = (await context.Pilotos.FindAsync(app.PilotA))!;
        position.UltimaUbicacionAt = app.Clock.GetUtcNow().UtcDateTime; await context.SaveChangesAsync(); handler.Reset();
        var ids = orders.Select(o => o.OrdenId).ToArray(); var batch = await service.SeguimientoPedidosAsync(managerActor, ids);
        Assert.Equal(20, batch.Count); Assert.All(batch.Values, estimate => Assert.NotNull(estimate)); Assert.Equal(1, handler.Calls);
        Assert.Equal(.4, batch[orders[0].OrdenId]!.Minutos); Assert.Equal(8, batch[orders[^1].OrdenId]!.Minutos);
        Assert.NotNull(await service.SeguimientoPedidoAsync(managerActor, orders[12].OrdenId));
        await service.SnapshotAsync(managerActor); Assert.Equal(1, handler.Calls);
        await coreService.ConfirmarEntregaParadaAsync(orders[0].OrdenId, orders[0].Paradas[0].ParadaId, orders[0].Paradas[0].CodigoC, "pilot-a", Guid.NewGuid().ToString("N"));
        var after = await service.SeguimientoPedidosAsync(managerActor, ids);
        Assert.Null(after[orders[0].OrdenId]); Assert.All(after.Where(x => x.Key != orders[0].OrdenId), x => Assert.NotNull(x.Value)); Assert.Equal(2, handler.Calls);
        Assert.Empty(await service.SeguimientoPedidosAsync(await service.PilotAsync("pilot-b"), ids));
        Assert.Equal(400, (await Assert.ThrowsAsync<OperationsException>(() => service.SeguimientoPedidosAsync(managerActor, Enumerable.Range(1, 51).ToArray()))).StatusCode);
    }
}
