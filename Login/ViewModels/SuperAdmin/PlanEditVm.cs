using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.SuperAdmin
{
    public class PlanEditVm
    {
        [Required]
        public int Id { get; set; }

        [Required, MaxLength(80)]
        public string Nombre { get; set; } = "";

        [Range(0, 999999999)]
        public decimal PrecioMensual { get; set; }

        [Range(0, 100000)]
        public int MaxTiendas { get; set; }

        [Range(0, 100000)]
        public int MaxPilotos { get; set; }

        [Range(0, 1000000)]
        public int MaxOrdenesPorMes { get; set; }

        public bool Activo { get; set; }
    }
}
