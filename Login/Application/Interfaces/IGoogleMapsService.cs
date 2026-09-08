using System.Threading.Tasks;

namespace Login.Application.Interfaces
{
    /// <summary>
    /// Cliente de Google Maps para geocodificar direcciones y calcular distancias (km).
    /// </summary>
    public interface IGoogleMapsService
    {
        /// <summary>Geocodifica una dirección y devuelve lat/lng (null si no hay key o falla).</summary>
        Task<(double Lat, double Lng)?> GeocodificarAsync(string direccion);

        /// <summary>Calcula la distancia real de ruta (km) con Routes API (null si no hay key o falla).</summary>
        Task<double?> DistanciaRutaKmAsync(double latOrigen, double lngOrigen, double latDestino, double lngDestino);

        /// <summary>Calcula la distancia en km entre dos coordenadas (línea recta, Haversine, sin API).</summary>
        double CalcularDistanciaKm(double latOrigen, double lngOrigen, double latDestino, double lngDestino);

        /// <summary>API key configurada (para el JS del mapa en las vistas).</summary>
        string ApiKey { get; }

        /// <summary>API key para el navegador (Maps JavaScript). Se expone al cliente.</summary>
        string BrowserApiKey { get; }
    }
}
