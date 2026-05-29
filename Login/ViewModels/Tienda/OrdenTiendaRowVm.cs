using System;

namespace Login.ViewModels.Tienda
{
    public class OrdenTiendaRowVm
    {
        public int OrdenId { get; set; }
        public int NumeroOrdenA { get; set; }
        public string Estado { get; set; } = "";
        public int EstadoNum { get; set; }
        public string UsuarioFinal { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        // Tracking timestamps
        public DateTime? AssignedAt { get; set; }
        public DateTime? RecolectadaAt { get; set; }
        public DateTime? EntregadaAt { get; set; }
    }
}
