using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Login.ViewModels.Supervisor
{
    public class OrdenesIndexVm
    {
        // Filtros
        public int? TiendaId { get; set; }
        public int? PilotoId { get; set; }
        public string? Estado { get; set; }  // string para UI simple
        public int? NumeroOrdenA { get; set; }

        // Dropdowns
        public List<SelectListItem> Tiendas { get; set; } = new();
        public List<SelectListItem> Pilotos { get; set; } = new();
        public List<SelectListItem> Estados { get; set; } = new();

        // Tabla
        public List<OrdenRowVm> Ordenes { get; set; } = new();

        // Mensajes
        public string? Ok { get; set; }
        public string? Error { get; set; }
    }
}
