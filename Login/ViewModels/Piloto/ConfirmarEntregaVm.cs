using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Piloto
{
    public class ConfirmarEntregaVm
    {
        [Required]
        public string CodigoB { get; set; } = string.Empty;

        [Required]
        public string CodigoC { get; set; } = string.Empty;
    }
}
