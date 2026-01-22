using System.ComponentModel.DataAnnotations;

namespace Login.Domain.Entities
{
    public class Plan
    {
        public int Id { get; set; }

        [Required, MaxLength(80)]
        public string Nombre { get; set; } = string.Empty;

        public decimal PrecioMensual { get; set; }

        public int MaxTiendas { get; set; }
        public int MaxPilotos { get; set; }
        public int MaxOrdenesPorMes { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ClienteApp> ClientesApp { get; set; } = new List<ClienteApp>();
    }
}
