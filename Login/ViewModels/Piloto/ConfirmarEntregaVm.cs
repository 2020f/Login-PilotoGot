using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Piloto
{
    public class ConfirmarEntregaVm
    {
        public int? OrdenId { get; set; }
        public int? ParadaId { get; set; }
        public string? OperacionId { get; set; }

        public string CodigoB { get; set; } = string.Empty;

        [Required]
        public string CodigoC { get; set; } = string.Empty;
    }
}
