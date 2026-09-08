using System;
using System.Globalization;

namespace Login.Application.Services
{
    public static class MapaLinkParser
    {
        /// <summary>
        /// Extrae lat/lng de un enlace de Google Maps tipo
        /// https://maps.google.com/maps?q=18.4812053,-69.9743033&amp;z=17&amp;hl=es
        /// </summary>
        public static (double? Lat, double? Lng)? Parse(string? mapaLink)
        {
            if (string.IsNullOrWhiteSpace(mapaLink))
                return null;

            try
            {
                var uri = new Uri(mapaLink);
                var q = uri.Query;

                var idx = q.IndexOf("q=", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return null;

                var value = q.Substring(idx + 2);
                var amp = value.IndexOf('&');
                if (amp >= 0) value = value.Substring(0, amp);

                value = Uri.UnescapeDataString(value).Trim();

                var partes = value.Split(',');
                if (partes.Length < 2) return null;

                if (double.TryParse(partes[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(partes[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                {
                    return (lat, lng);
                }
            }
            catch
            {
                // ignorar
            }

            return null;
        }
    }
}
