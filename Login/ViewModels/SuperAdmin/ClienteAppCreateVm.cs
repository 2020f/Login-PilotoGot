using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.SuperAdmin
{
    public class ClienteAppCreateVm
    {
        [Required, MaxLength(120)]
        public string NombreComercial { get; set; } = "";

        [Required]
        public int PlanId { get; set; }
    }
}
