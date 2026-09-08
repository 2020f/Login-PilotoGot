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
        public int EstadoNum { get; set; }
        public double? Latitud { get; set; }
        public double? Longitud { get; set; }
        public DateTime? UltimaUbicacionAt { get; set; }
    }
}
