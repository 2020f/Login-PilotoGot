using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Login.Application.Services
{
    public class GoogleMapsService : IGoogleMapsService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _browserApiKey;

        public GoogleMapsService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["GoogleMaps:ApiKey"] ?? "";
            _browserApiKey = config["GoogleMaps:BrowserApiKey"] ?? "";
        }

        public string ApiKey => _apiKey;

        public string BrowserApiKey
            => _browserApiKey;

        private bool HayKey()
            => !string.IsNullOrWhiteSpace(_apiKey)
               && !_apiKey.Contains("REEMPLAZAR", StringComparison.OrdinalIgnoreCase);

        public async Task<(double Lat, double Lng)?> GeocodificarAsync(string direccion)
        {
            if (!HayKey() || string.IsNullOrWhiteSpace(direccion))
                return null;

            var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(direccion)}&key={_apiKey}";

            try
            {
                using var resp = await _http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    return null;

                var loc = results[0].GetProperty("geometry").GetProperty("location");
                var lat = loc.GetProperty("lat").GetDouble();
                var lng = loc.GetProperty("lng").GetDouble();
                return (lat, lng);
            }
            catch
            {
                return null;
            }
        }

        public async Task<double?> DistanciaRutaKmAsync(double latOrigen, double lngOrigen, double latDestino, double lngDestino)
        {
            if (!HayKey()) return null;

            const string url = "https://routes.googleapis.com/directions/v2:computeRoutes";

            var body = JsonSerializer.Serialize(new
            {
                origin = new { location = new { latLng = new { latitude = latOrigen, longitude = lngOrigen } } },
                destination = new { location = new { latLng = new { latitude = latDestino, longitude = lngDestino } } },
                travelMode = "DRIVE"
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("X-Goog-Api-Key", _apiKey);
            request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters");
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            try
            {
                using var resp = await _http.SendAsync(request);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
                    return null;

                if (!routes[0].TryGetProperty("distanceMeters", out var distMeters))
                    return null;

                return Math.Round(distMeters.GetDouble() / 1000.0, 2);
            }
            catch
            {
                return null;
            }
        }

        public double CalcularDistanciaKm(double latOrigen, double lngOrigen, double latDestino, double lngDestino)
        {
            const double radioTierraKm = 6371.0;

            double ToRad(double grados) => grados * Math.PI / 180.0;

            var dLat = ToRad(latDestino - latOrigen);
            var dLng = ToRad(lngDestino - lngOrigen);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRad(latOrigen)) * Math.Cos(ToRad(latDestino)) *
                    Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return Math.Round(radioTierraKm * c, 2);
        }
    }
}
