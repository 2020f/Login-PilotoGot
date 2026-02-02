using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;

namespace Login.Domain.Entities
{
    public class ClienteApp
    {
        public int Id { get; set; }

        [Required, MaxLength(120)]
        public string NombreComercial { get; set; } = string.Empty;


        [MaxLength(250)]
        public string? Ubicacion { get; set; }

        [MaxLength(30)]
        public string? Telefono { get; set; }






        // =========================
        // Contacto / Identidad fiscal
        // =========================

        [EmailAddress, MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? RNC { get; set; }

        [MaxLength(120)]
        public string? ContactoResponsable { get; set; }


        // =========================
        // Suscripción / Cobros
        // =========================

        public DateTime? FechaInicioPlan { get; set; }
        public DateTime? FechaFinPlan { get; set; }

        public DateTime? UltimoPago { get; set; }
        public DateTime? ProximoCobro { get; set; }

        public bool? EsTrial { get; set; }


        // =========================
        // Límites del sistema
        // =========================

        public int? MaxUsuarios { get; set; }
        public int? MaxOrdenesMes { get; set; }
        public int? MaxPilotos { get; set; }


        // =========================
        // Estado / Notas
        // =========================

        public bool? Activo { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }


        // =========================
        // Auditoría
        // =========================

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(100)]
        public string? UpdatedBy { get; set; }





































        public int PlanId { get; set; }
        public Plan Plan { get; set; } = null!;      

        public EstadoClienteApp Estado { get; set; } = EstadoClienteApp.Activo;

        // ✅ Contador para generar A secuencial por tenant
        public int OrdenSeq { get; set; } = 0;

        public string? GestorIdentityUserId { get; set; }


        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Tienda> Tiendas { get; set; } = new List<Tienda>();
        public ICollection<Piloto> Pilotos { get; set; } = new List<Piloto>();
    }
}
