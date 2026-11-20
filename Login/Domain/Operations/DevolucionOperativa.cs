using System.ComponentModel.DataAnnotations;
using Login.Domain.Entities;
using Login.Domain.Enums;

namespace Login.Domain.Operations;

public enum TipoGestionFalla { Reintento = 1, Devolucion = 2 }
public enum EstadoDevolucionOperativa { Solicitada = 1, Autorizada = 2, EnRetorno = 3, Recibida = 4, Rechazada = 5, ReintentoAutorizado = 6, TrasladadaAReasignacion = 7 }

public class DevolucionOperativa : OperativoTenant
{
    public int OrdenEntregaId { get; set; }
    public OrdenEntrega OrdenEntrega { get; set; } = null!;
    public int ParadaEntregaId { get; set; }
    public ParadaEntrega ParadaEntrega { get; set; } = null!;
    public int PilotoId { get; set; }
    public Piloto Piloto { get; set; } = null!;
    public TipoGestionFalla Tipo { get; set; }
    public EstadoDevolucionOperativa Estado { get; set; } = EstadoDevolucionOperativa.Solicitada;
    public bool Abierta { get; set; } = true;
    [MaxLength(1000)] public string Motivo { get; set; } = "";
    [MaxLength(450)] public string SolicitadaPor { get; set; } = "";
    [MaxLength(450)] public string? DecididaPor { get; set; }
    [MaxLength(1000)] public string? NotaDecision { get; set; }
    public DateTime? AutorizadaAt { get; set; }
    public DateTime? IniciadaAt { get; set; }
    public DateTime? RecibidaAt { get; set; }
    public int? TiendaRecepcionId { get; set; }
    [MaxLength(450)] public string? RecibidaPor { get; set; }
    [MaxLength(1000)] public string? ObservacionesRecepcion { get; set; }
}

public static class EstadosEntrega
{
    public static readonly EstadoOrden[] Terminales = [EstadoOrden.Entregada, EstadoOrden.Devuelta, EstadoOrden.FinalizadaConDevoluciones, EstadoOrden.Cancelada];
    public static readonly EstadoParadaEntrega[] ParadasTerminales = [EstadoParadaEntrega.Entregada, EstadoParadaEntrega.Devuelta, EstadoParadaEntrega.Cancelada];
    public static bool EsTerminal(EstadoOrden estado) => Terminales.Contains(estado);
    public static bool EsTerminal(EstadoParadaEntrega estado) => ParadasTerminales.Contains(estado);
}
