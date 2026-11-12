using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Envirapido;

public sealed class ImportacionVm
{
    public string Empresa { get; set; } = "";
    public List<ImportacionFilaVm> Filas { get; set; } = [];
    public List<ImportacionHistorialVm> Historial { get; set; } = [];
    public string? Error { get; set; }
    public bool Confirmada { get; set; }
}
public sealed class ImportacionFilaVm
{
    public int Linea { get; set; }
    public int ComercioId { get; set; }
    [MaxLength(1200)] public string DestinatarioIds { get; set; } = "";
    [MaxLength(400)] public string? Nota { get; set; }
    [MaxLength(64)] public string OperacionId { get; set; } = Guid.NewGuid().ToString("N");
    public string? Error { get; set; }
    public int? OrdenId { get; set; }
    public string? Codigo { get; set; }
}
public sealed class ImportacionHistorialVm
{
    public int OrdenId { get; set; }
    public string Codigo { get; set; } = "";
    public DateTime CreadoUtc { get; set; }
}
