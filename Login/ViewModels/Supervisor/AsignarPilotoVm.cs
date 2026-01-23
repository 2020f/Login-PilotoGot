using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Supervisor
{
    public class AsignarPilotoVm
    {
        [Required]
        public string CodigoB { get; set; } = string.Empty;

        [Required]
        public int PilotoId { get; set; }
    }
}
