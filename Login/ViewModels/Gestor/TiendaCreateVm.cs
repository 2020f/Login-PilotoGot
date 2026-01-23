using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Gestor
{
    public class TiendaCreateVm
    {
        [Required, MaxLength(120)]
        public string Nombre { get; set; } = "";

        [Required, MaxLength(200)]
        public string Direccion { get; set; } = "";

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = "";
    }
}
