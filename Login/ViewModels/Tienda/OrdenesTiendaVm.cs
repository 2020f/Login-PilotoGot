using System.Collections.Generic;

namespace Login.ViewModels.Tienda
{
    public class OrdenesTiendaVm
    {
        public int TiendaId { get; set; }
        public List<OrdenTiendaRowVm> Ordenes { get; set; } = new();
    }
}
