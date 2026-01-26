using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;

namespace Login.ViewModels.SuperAdmin
{
    public class ClienteAppEditVm
    {
        [Required]
        public int Id { get; set; }

        [Required, MaxLength(120)]
        public string NombreComercial { get; set; } = "";



        [MaxLength(250)]
        public string? Ubicacion { get; set; }

        [MaxLength(30)]
        public string? Telefono { get; set; }


        [Required]
        public int PlanId { get; set; }

        [Required]
        public EstadoClienteApp Estado { get; set; }
    }
}
