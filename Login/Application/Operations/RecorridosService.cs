using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Login.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Login.Application.Operations;

// Los importes de cada tramo son acumulados desde el GPS muestreado. La geometría
// guarda sólo ese leg; los prefijos se construyen al responder, sin caché O(N²).
public sealed record TramoRecorridoEstimado(int PuntoId, double DistanciaKmAcumulada,
    double MinutosAcumulados, string GeometriaTramo);
public sealed record RecorridoEstimado(double DistanciaKm, double Minutos, string Geometria, DateTime CalculadoAt,
    IReadOnlyList<TramoRecorridoEstimado>? Tramos = null)
{
    public RecorridoEstimado? HastaPunto(int puntoId)
    {
        if (Tramos is null) return null;
        var index = -1;
        for (var i = 0; i < Tramos.Count; i++) if (Tramos[i].PuntoId == puntoId) { index = i; break; }
        if (index < 0) return null;
        var prefix = Tramos.Take(index + 1).ToArray(); var last = prefix[^1];
        return new(last.DistanciaKmAcumulada, last.MinutosAcumulados,
            JsonSerializer.Serialize(prefix.Select(p => p.GeometriaTramo)), CalculadoAt, prefix);
    }
}

public sealed class RecorridosService(HttpClient http, IGoogleMapsService maps, IMemoryCache cache, TimeProvider clock, IConfiguration configuration)
{
    private static readonly object CacheGate = new();
    public async Task<RecorridoEstimado?> EstimarAsync(int rutaId, string revision, double? lat, double? lng,
        DateTime? posicionAt, IReadOnlyList<PuntoRutaDto> points, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow().UtcDateTime;
        if (string.IsNullOrWhiteSpace(maps.ApiKey) || !Coordinate(lat, lng)
            || posicionAt is null || posicionAt < now.AddSeconds(-OperationsGpsPolicy.FreshnessSeconds(configuration))
            || points.Count == 0 || points.Any(p => !p.PuedeVerDestino || !Coordinate(p.Latitud, p.Longitud))
            || points.Select(p => p.Id).Distinct().Count() != points.Count) return null;
        // Completa y ordenada: cualquier cambio de progreso/orden/coordenadas invalida.
        // El GPS se muestrea como máximo una vez por minuto para esta revisión.
        var progress = string.Join(";", points.Select(p => $"{p.Id}:{p.Latitud!.Value.ToString("R", CultureInfo.InvariantCulture)}:{p.Longitud!.Value.ToString("R", CultureInfo.InvariantCulture)}"));
        var key = $"envirapido-route-v2:{rutaId}:{revision}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(progress)))}";
        Lazy<Task<EstimateBox>> pending;
        lock (CacheGate)
        {
            if (!cache.TryGetValue(key, out Lazy<Task<EstimateBox>>? existing) || existing is null)
            {
                Lazy<Task<EstimateBox>>? created = null;
                created = new(async () =>
                {
                    try { return await ComputeAsync(); }
                    finally
                    {
                        lock (CacheGate)
                        {
                            if (cache.TryGetValue(key, out Lazy<Task<EstimateBox>>? current) && ReferenceEquals(current, created))
                                cache.Set(key, created!, TimeSpan.FromMinutes(1));
                        }
                    }
                }, LazyThreadSafetyMode.ExecutionAndPublication);
                pending = created;
                // Una ruta larga no expira mientras todavía usa el proveedor.
                cache.Set(key, pending);
            }
            else pending = existing;
        }
        // Cancelar una pestaña/fila no cancela la consulta compartida de otros usuarios.
        var result = await pending.Value.WaitAsync(cancellationToken);
        // Un GPS nuevo del solicitante no convierte en reciente el origen viejo
        // de una estimación compartida. Se conserva el TTL sin presentar ETA falsa.
        return result.MuestraGpsAt < clock.GetUtcNow().UtcDateTime.AddSeconds(-OperationsGpsPolicy.FreshnessSeconds(configuration))
            ? null : result.Value;

        async Task<EstimateBox> ComputeAsync()
        {
            var path = new List<GeoPoint> { new(lat!.Value, lng!.Value) };
            path.AddRange(points.Select(p => new GeoPoint(p.Latitud!.Value, p.Longitud!.Value)));
            var meters = 0d; var seconds = 0d; var geometries = new List<string>();
            var cumulativeMeters = 0d; var cumulativeSeconds = 0d;
            var legs = new List<TramoRecorridoEstimado>();
            try
            {
                // Veinte legs por llamada mantienen el límite del proveedor. Nunca
                // reordenar puntos ni sustituir faltantes por distancias en línea recta.
                for (var start = 0; start < path.Count - 1; start += 20)
                {
                    var segment = path.Skip(start).Take(21).ToArray();
                    object Waypoint(GeoPoint p) => new { location = new { latLng = new { latitude = p.Latitud, longitude = p.Longitud } } };
                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes");
                    request.Headers.Add("X-Goog-Api-Key", maps.ApiKey);
                    request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline,routes.legs.distanceMeters,routes.legs.duration,routes.legs.polyline.encodedPolyline");
                    request.Content = JsonContent.Create(new { origin = Waypoint(segment[0]), destination = Waypoint(segment[^1]),
                        intermediates = segment.Skip(1).SkipLast(1).Select(Waypoint).ToArray(), travelMode = "DRIVE",
                        routingPreference = "TRAFFIC_AWARE", optimizeWaypointOrder = false, polylineEncoding = "ENCODED_POLYLINE" });
                    using var response = await http.SendAsync(request);
                    if (!response.IsSuccessStatusCode) return new(null);
                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    var routes = doc.RootElement.GetProperty("routes");
                    if (routes.GetArrayLength() == 0) return new(null);
                    var route = routes[0];
                    var distance = route.GetProperty("distanceMeters").GetDouble();
                    if (!Positive(distance) || !Duration(route.GetProperty("duration"), out var duration)) return new(null);
                    var fullGeometry = route.GetProperty("polyline").GetProperty("encodedPolyline").GetString();
                    var providerLegs = route.GetProperty("legs");
                    if (string.IsNullOrWhiteSpace(fullGeometry) || providerLegs.GetArrayLength() != segment.Length - 1) return new(null);
                    meters += distance; seconds += duration; geometries.Add(fullGeometry);
                    var index = 0;
                    foreach (var leg in providerLegs.EnumerateArray())
                    {
                        var legMeters = leg.GetProperty("distanceMeters").GetDouble();
                        if (!Positive(legMeters) || !Duration(leg.GetProperty("duration"), out var legSeconds)) return new(null);
                        var geometry = leg.GetProperty("polyline").GetProperty("encodedPolyline").GetString();
                        if (string.IsNullOrWhiteSpace(geometry)) return new(null);
                        cumulativeMeters += legMeters; cumulativeSeconds += legSeconds;
                        if (!Positive(cumulativeMeters) || !Positive(cumulativeSeconds)
                            || cumulativeSeconds / 60 > (DateTime.MaxValue - now).TotalMinutes) return new(null);
                        legs.Add(new(points[start + index].Id, Math.Round(cumulativeMeters / 1000, 2), Math.Round(cumulativeSeconds / 60, 1), geometry));
                        index++;
                    }
                }
                if (!Positive(meters) || !Positive(seconds) || seconds / 60 > (DateTime.MaxValue - now).TotalMinutes) return new(null);
                return new(new(Math.Round(meters / 1000, 2), Math.Round(seconds / 60, 1), JsonSerializer.Serialize(geometries), now, legs), posicionAt);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { return new(null); }
        }
    }
    private static bool Positive(double value) => double.IsFinite(value) && value >= 0;
    private static bool Coordinate(double? lat, double? lng) => lat.HasValue && lng.HasValue
        && double.IsFinite(lat.Value) && double.IsFinite(lng.Value) && lat.Value is >= -90 and <= 90 && lng.Value is >= -180 and <= 180;
    private static bool Duration(JsonElement element, out double seconds)
    {
        seconds = 0; var text = element.GetString();
        return text is not null && text.EndsWith('s') && double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.Float,
            CultureInfo.InvariantCulture, out seconds) && Positive(seconds);
    }
    private sealed record EstimateBox(RecorridoEstimado? Value, DateTime? MuestraGpsAt = null);
}
