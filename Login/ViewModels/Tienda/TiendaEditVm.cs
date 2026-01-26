using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Tienda
{
    public class TiendaEditVm
    {
        [Required]
        public int Id { get; set; }

        [Required, MaxLength(120)]
        public string Nombre { get; set; } = "";

        [Required, MaxLength(200)]
        public string Direccion { get; set; } = "";

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = "";

        public bool Activo { get; set; } = true;

        // Solo mostrar (login de la tienda)
        public string? EmailCliente { get; set; }
    }
}
