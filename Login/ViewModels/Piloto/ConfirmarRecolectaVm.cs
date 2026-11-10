using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Piloto
{
    public class ConfirmarRecolectaVm
    {
        public int? OrdenId { get; set; }

        [Required]
        public string CodigoB { get; set; } = string.Empty;
    }
}
