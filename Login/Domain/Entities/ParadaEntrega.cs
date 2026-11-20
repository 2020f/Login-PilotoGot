using System.ComponentModel.DataAnnotations;

namespace Login.Domain.Entities;

public enum EstadoParadaEntrega { Pendiente = 1, Entregada = 2, DevolucionAutorizada = 3, EnRetorno = 4, Devuelta = 5, Cancelada = 6 }

public class ParadaEntrega
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public int OrdenEntregaId { get; set; }
    public OrdenEntrega OrdenEntrega { get; set; } = null!;
    public int UsuarioFinalId { get; set; }
    public int TiendaId { get; set; }
    public UsuarioFinal UsuarioFinal { get; set; } = null!;
    public int Posicion { get; set; }
    [MaxLength(120)] public string Nombre { get; set; } = "";
    [MaxLength(250)] public string Direccion { get; set; } = "";
    [MaxLength(30)] public string Telefono { get; set; } = "";
    [MaxLength(500)] public string? MapaLink { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    [MaxLength(400)] public string? Instrucciones { get; set; }
    public EstadoParadaEntrega Estado { get; set; } = EstadoParadaEntrega.Pendiente;
    public DateTime? EntregadaAt { get; set; }
    public DateTime? DevueltaAt { get; set; }
    public int? PilotoAsignadoId { get; set; }
    public int? PilotoCustodioId { get; set; }
    public int? PilotoEntregaId { get; set; }
    public DateTime? RecogidaParadaAt { get; set; }
    [MaxLength(32)] public string RevisionAsignacion { get; set; } = "";
    public bool EnReasignacion { get; set; }
    [MaxLength(120)] public string? RecogidaActualNombre { get; set; }
    [MaxLength(250)] public string? RecogidaActualDireccion { get; set; }
    public double? RecogidaActualLatitud { get; set; }
    public double? RecogidaActualLongitud { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<CodigoEntrega> Codigos { get; set; } = new List<CodigoEntrega>();
}
