using System;

namespace Login.ViewModels.Piloto
{
    public class MisOrdenesRowVm
    {
        public int OrdenId { get; set; }
        public int NumeroOrdenA { get; set; }
        public string Estado { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public string CodigoB { get; set; } = "";
        public bool PuedeVerDestino { get; set; }

        public string? NombreUsuarioFinal { get; set; }
        public string? TelefonoUsuarioFinal { get; set; }
        public string? DireccionUbicacion { get; set; }
    }
}
