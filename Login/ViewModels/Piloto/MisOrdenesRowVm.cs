using System;

namespace Login.ViewModels.Piloto
{
    public class MisOrdenesRowVm
    {
        public int OrdenId { get; set; }
        public int NumeroOrdenA { get; set; }
        public string? CodigoA { get; set; }
        public string CodigoVisibleA => CodigoA ?? NumeroOrdenA.ToString();
        public string Estado { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public bool PuedeVerDestinoFinal { get; set; }

        // Antes del scan (Asignada): datos de tienda + nota
        public string TiendaNombre { get; set; } = "";
        public string TiendaDireccion { get; set; } = "";
        public string TiendaTelefono { get; set; } = "";
        public string? NotaPedido { get; set; }

        // Después del scan (Recolectada): destino final
        public string UsuarioFinalNombre { get; set; } = "";
        public string UsuarioFinalDireccion { get; set; } = "";
        public string? UsuarioFinalMapaLink { get; set; }
        public string UsuarioFinalTelefono { get; set; } = "";

        // Precio / distancia
        public decimal? Precio { get; set; }
        public double? DistanciaKm { get; set; }
    }
}
