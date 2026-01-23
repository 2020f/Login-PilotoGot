using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Login.ViewModels.Gestor
{
    public class AsignarTiendaUsuarioVm
    {
        [Required, EmailAddress]
        public string EmailUsuario { get; set; } = string.Empty;

        [Required]
        public int TiendaId { get; set; }

        public List<SelectListItem> Tiendas { get; set; } = new();

        public string? Ok { get; set; }
        public string? Error { get; set; }
    }
}
