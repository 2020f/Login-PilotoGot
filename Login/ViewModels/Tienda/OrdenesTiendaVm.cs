using System.Collections.Generic;

namespace Login.ViewModels.Tienda
{
    public class OrdenesTiendaVm
    {
        public int TiendaId { get; set; }
        public List<OrdenTiendaRowVm> Ordenes { get; set; } = new();
        public List<OrdenTiendaRowVm> Activas { get; set; } = new();
        public List<OrdenTiendaRowVm> Historial { get; set; } = new();
        public int PaginaHistorial { get; set; } = 1;
        public int TamanoHistorial { get; set; } = 30;
        public int TotalHistorial { get; set; }
        public int TotalPaginasHistorial { get; set; }
        public string? BuscarHistorial { get; set; }
        public Login.Application.Interfaces.ResumenOrdenesTiendaDto Resumen { get; set; } = new(0, 0, 0, 0, 0, 0);
    }
}
