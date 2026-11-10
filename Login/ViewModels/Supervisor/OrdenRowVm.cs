using System;


namespace Login.ViewModels.Supervisor
{
    public class OrdenRowVm
    {
        public int OrdenId { get; set; }
        public int ClienteAppId { get; set; }
        public int TiendaId { get; set; }
        public string TiendaNombre { get; set; } = "";
        public int NumeroOrdenA { get; set; }
        public string? CodigoA { get; set; }
        public string CodigoVisibleA => CodigoA ?? NumeroOrdenA.ToString();
        public string Estado { get; set; } = "";
        public int? PilotoId { get; set; }
        public string? PilotoNombre { get; set; }
        public string? PilotoTelefono { get; set; }

        public string? TiendaDireccion { get; set; }
        public string? TiendaTelefono { get; set; }
        public string? Notas { get; set; } // <- OJO: este nombre debe coincidir con tu entidad real

        // Auto-asignación
        public bool TiendaAutoAsignacion { get; set; }
        public bool EsAuto { get; set; }

        // Precio / distancia
        public decimal? Precio { get; set; }
        public double? DistanciaKm { get; set; }

        public DateTime CreatedAt { get; set; }

        // Para poder asignar:
        public string? CodigoB { get; set; }
    }
}
