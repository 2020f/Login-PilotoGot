using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Piloto
{
    public class ConfirmarRecolectaVm
    {
        [Required]
        public string CodigoB { get; set; } = string.Empty;
    }
}
