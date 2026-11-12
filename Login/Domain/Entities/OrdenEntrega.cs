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
        public int? UsuarioFinalId { get; set; }
        public UsuarioFinal? UsuarioFinal { get; set; }

        // La migración congela también los registros previos con los datos disponibles.
        // La bandera distingue NULL legítimo (por ejemplo coordenadas) del legado sin migrar.
        public bool DatosEntregaCongelados { get; set; }
        [MaxLength(120)] public string RecogidaNombre { get; set; } = "";
        [MaxLength(200)] public string RecogidaDireccion { get; set; } = "";
        [MaxLength(30)] public string RecogidaTelefono { get; set; } = "";
        public double? RecogidaLatitud { get; set; }
        public double? RecogidaLongitud { get; set; }
        [MaxLength(120)] public string DestinoNombre { get; set; } = "";
        [MaxLength(250)] public string DestinoDireccion { get; set; } = "";
        [MaxLength(30)] public string DestinoTelefono { get; set; } = "";
        [MaxLength(500)] public string? DestinoMapaLink { get; set; }
        public double? DestinoLatitud { get; set; }
        public double? DestinoLongitud { get; set; }

        // Asignación (nullable)
        public int? PilotoId { get; set; }
        public Piloto? Piloto { get; set; }

        // ✅ A secuencial por ClienteApp (UNIQUE por tenant)
        public int NumeroOrdenA { get; set; }

        // Null conserva el número A ya emitido en órdenes anteriores a la migración.
        [MaxLength(4)]
        public string? CodigoA { get; set; }

        public EstadoOrden Estado { get; set; } = EstadoOrden.Creada;

        // ✅ Orden creada en modo auto-asignación (los pilotos la aceptan/rechazan)
        public bool EsAuto { get; set; } = false;

        [MaxLength(400)]
        public string? NotaPedido { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AssignedAt { get; set; }
        public DateTime? RecolectadaAt { get; set; }
        public DateTime? EntregadaAt { get; set; }
        public DateTime? FinalizadaAt { get; set; }

        // ✅ Precio calculado (base + km) y distancia
        public decimal? Precio { get; set; }
        public double? DistanciaKm { get; set; }

        public ICollection<CodigoEntrega> Codigos { get; set; } = new List<CodigoEntrega>();
        public ICollection<HistorialOrden> Historial { get; set; } = new List<HistorialOrden>();
        public ICollection<ParadaEntrega> Paradas { get; set; } = new List<ParadaEntrega>();
    }
}
