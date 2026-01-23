using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Tienda
{
    public class UsuarioFinalCreateVm
    {
        public int TiendaId { get; set; }

        [Required, MaxLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MaxLength(250)]
        public string DireccionUbicacion { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = string.Empty;

        [MaxLength(400)]
        public string? Notas { get; set; }
    }
}
