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
        public string Estado { get; set; } = "";
        public int? PilotoId { get; set; }
        public string? PilotoNombre { get; set; }
        public DateTime CreatedAt { get; set; }

        // Para poder asignar:
        public string? CodigoB { get; set; }
    }
}
