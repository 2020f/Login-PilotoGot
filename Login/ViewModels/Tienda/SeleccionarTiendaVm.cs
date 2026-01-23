using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Login.ViewModels.Tienda
{
    public class SeleccionarTiendaVm
    {
        public string? Ok { get; set; }
        public string? Error { get; set; }
        public List<SelectListItem> Tiendas { get; set; } = new();
    }
}
