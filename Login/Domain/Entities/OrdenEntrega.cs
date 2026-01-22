using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;

namespace Login.Domain.Entities
{
    public class OrdenEntrega
    {
        public int Id { get; set; }

        // ✅ para filtrar rápido por tenant
        public int ClienteAppId { get; set; }
        public ClienteApp ClienteApp { get; set; } = null!;

        // Pickup
        public int TiendaId { get; set; }
        public Tienda Tienda { get; set; } = null!;

        // Delivery
        public int UsuarioFinalId { get; set; }
        public UsuarioFinal UsuarioFinal { get; set; } = null!;

        // Asignación (nullable)
        public int? PilotoId { get; set; }
        public Piloto? Piloto { get; set; }

        // ✅ A secuencial por ClienteApp (UNIQUE por tenant)
        public int NumeroOrdenA { get; set; }

        public EstadoOrden Estado { get; set; } = EstadoOrden.Creada;

        [MaxLength(400)]
        public string? NotaPedido { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AssignedAt { get; set; }
        public DateTime? RecolectadaAt { get; set; }
        public DateTime? EntregadaAt { get; set; }

        public ICollection<CodigoEntrega> Codigos { get; set; } = new List<CodigoEntrega>();
        public ICollection<HistorialOrden> Historial { get; set; } = new List<HistorialOrden>();
    }
}
