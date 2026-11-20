using Login.Application.Operations;
using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Envirapido;

public sealed class OperationsPageVm
{
    public string Empresa { get; set; } = "";
    public string Seccion { get; set; } = "";
    public string Tab { get; set; } = "";
    public string? Buscar { get; set; }
    public int? Id { get; set; }
    public int? OrdenId { get; set; }
    public int? ParadaId { get; set; }
    public int? RutaId { get; set; }
    public OperationsSnapshot Datos { get; set; } = null!;
    public PanelVm Operacion { get; set; } = new();
    public List<DevolucionOperativaDto> Devoluciones { get; set; } = [];
    public List<RetiroParadaDto> GestionesRetiro { get; set; } = [];
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
    public List<PanelOrderVm> CandidatasRuta { get; set; } = [];
    public DateOnly? Dia { get; set; }
}

public sealed class CrearEnvioPanelVm
{
    [Range(1,int.MaxValue)] public int ComercioId { get; set; }
    [MaxLength(400)] public string? Nota { get; set; }
    [MaxLength(64)] public string OperacionId { get; set; } = Guid.NewGuid().ToString("N");
    public List<Login.ViewModels.Tienda.CrearParadaVm> Destinatarios { get; set; } = [];
    public PanelVm Panel { get; set; } = new();
    public List<PanelRecipientVm> Personas { get; set; } = [];
}
public sealed class PanelRecipientVm
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
}
