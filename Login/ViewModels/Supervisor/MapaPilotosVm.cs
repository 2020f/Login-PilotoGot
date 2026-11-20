using System.Collections.Generic;

namespace Login.ViewModels.Supervisor
{
    public class MapaPilotosVm
    {
        public decimal PrecioBase { get; set; }
        public decimal PrecioPorKm { get; set; }
        public string? Ok { get; set; }
        public string? Error { get; set; }
        public List<PilotoMapaVm> Pilotos { get; set; } = new();
    }

    public class PilotoMapaVm
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Estado { get; set; } = "";
        public bool Activo { get; set; }
        public bool EnLinea { get; set; }
        public int OrdenesActivas { get; set; }
        public int EstadoNum { get; set; }
        public double? Latitud { get; set; }
        public double? Longitud { get; set; }
        public DateTime? UltimaUbicacionAt { get; set; }
        public double? EdadGpsSegundos => UltimaUbicacionAt.HasValue
            ? Math.Max(0, (DateTime.UtcNow - DateTime.SpecifyKind(UltimaUbicacionAt.Value, DateTimeKind.Utc)).TotalSeconds) : null;
        public bool GpsVigente => Activo && Latitud.HasValue && Longitud.HasValue && EdadGpsSegundos <= 60;
    }
}
