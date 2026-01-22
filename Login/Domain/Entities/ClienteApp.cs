using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;

namespace Login.Domain.Entities
{
    public class ClienteApp
    {
        public int Id { get; set; }

        [Required, MaxLength(120)]
        public string NombreComercial { get; set; } = string.Empty;

        public int PlanId { get; set; }
        public Plan Plan { get; set; } = null!;

        public EstadoClienteApp Estado { get; set; } = EstadoClienteApp.Activo;

        // ✅ Contador para generar A secuencial por tenant
        public int OrdenSeq { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Tienda> Tiendas { get; set; } = new List<Tienda>();
        public ICollection<Piloto> Pilotos { get; set; } = new List<Piloto>();
    }
}
