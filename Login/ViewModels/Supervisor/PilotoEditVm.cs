using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;

namespace Login.ViewModels.Supervisor
{
    public class PilotoEditVm
    {
        [Required]
        public int Id { get; set; }

        [Required, MaxLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = string.Empty;

        public EstadoPiloto Estado { get; set; } = EstadoPiloto.Disponible;

        public bool Activo { get; set; } = true;

        // solo para mostrar (no editable)
        public string? EmailPiloto { get; set; }
    }
}
