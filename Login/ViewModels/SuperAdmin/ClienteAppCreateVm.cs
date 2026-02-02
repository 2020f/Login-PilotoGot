using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.SuperAdmin
{
    public class ClienteAppCreateVm
    {
        [Required, MaxLength(120)]
        public string NombreComercial { get; set; } = "";


        [MaxLength(250)]
        public string? Ubicacion { get; set; }

        [MaxLength(30)]
        public string? Telefono { get; set; }


        [Required]
        public int PlanId { get; set; }


        // NUEVOS (nullable)
        [EmailAddress, MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? RNC { get; set; }

        [MaxLength(120)]
        public string? ContactoResponsable { get; set; }

        public DateTime? FechaInicioPlan { get; set; }
        public DateTime? FechaFinPlan { get; set; }

        public DateTime? UltimoPago { get; set; }
        public DateTime? ProximoCobro { get; set; }

        public bool EsTrial { get; set; }

        public int? MaxUsuarios { get; set; }
        public int? MaxOrdenesMes { get; set; }
        public int? MaxPilotos { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }








    }
}
