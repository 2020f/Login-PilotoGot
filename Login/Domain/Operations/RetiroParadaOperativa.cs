using System.ComponentModel.DataAnnotations;
using Login.Domain.Entities;

namespace Login.Domain.Operations;

public class RetiroParadaOperativa : OperativoTenant
{
    public int OrdenEntregaId { get; set; }
    public OrdenEntrega OrdenEntrega { get; set; } = null!;
    public int ParadaEntregaId { get; set; }
    public ParadaEntrega ParadaEntrega { get; set; } = null!;
    public int? PilotoOrigenId { get; set; }
    public int? PilotoDestinoId { get; set; }
    public int? TiendaDestinoId { get; set; }
    public int? IncidenciaId { get; set; }
    [MaxLength(32)] public string Estado { get; set; } = "Solicitada";
    [MaxLength(16)] public string? TipoDestino { get; set; }
    [MaxLength(120)] public string? DestinoNombre { get; set; }
    [MaxLength(250)] public string? DestinoDireccion { get; set; }
    public double? DestinoLatitud { get; set; }
    public double? DestinoLongitud { get; set; }
    [MaxLength(1000)] public string Motivo { get; set; } = "";
    [MaxLength(1000)] public string? NotaDecision { get; set; }
    [MaxLength(450)] public string SolicitadaPor { get; set; } = "";
    [MaxLength(450)] public string? DecididaPor { get; set; }
    [MaxLength(450)] public string? RecibidaPor { get; set; }
    public DateTime? RecibidaAt { get; set; }
    public bool Abierta { get; set; } = true;
}
