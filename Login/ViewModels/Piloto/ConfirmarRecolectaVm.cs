using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Piloto
{
    public class ConfirmarRecolectaVm
    {
        public string? OperacionId { get; set; }
        public int? OrdenId { get; set; }

        [Required]
        public string CodigoB { get; set; } = string.Empty;
    }
}
