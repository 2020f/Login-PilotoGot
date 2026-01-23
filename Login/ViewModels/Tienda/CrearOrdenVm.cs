using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Login.ViewModels.Tienda
{
    public class CrearOrdenVm
    {
        public int ClienteAppId { get; set; }
        public int TiendaId { get; set; }

        [Required]
        public int UsuarioFinalId { get; set; }

        [MaxLength(400)]
        public string? NotaPedido { get; set; }

        public List<SelectListItem> UsuariosFinales { get; set; } = new();

        public string? Ok { get; set; }
        public string? Error { get; set; }

        // Resultado al crear:
        public int? OrdenId { get; set; }
        public int? NumeroOrdenA { get; set; }
        public string? CodigoB { get; set; }
        public string? CodigoC { get; set; }
    }
}
