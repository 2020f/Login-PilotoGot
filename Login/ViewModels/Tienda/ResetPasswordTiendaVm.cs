using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Tienda
{
    public class ResetPasswordTiendaVm
    {
        [Required]
        public int TiendaId { get; set; }

        [Required, MinLength(6)]
        public string NuevoPassword { get; set; } = "";
    }
}
