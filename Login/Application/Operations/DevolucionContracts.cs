namespace Login.Application.Operations;

public sealed record SolicitarDevolucionRequest(int OrdenId, int ParadaId, string Tipo, string Motivo, string OperacionId);
public sealed record DecidirDevolucionRequest(string Accion, string? Nota, string Version, string OperacionId);
public sealed record OperacionVersionRequest(string Version, string OperacionId);
public sealed record RecibirDevolucionRequest(int TiendaId, string Observaciones, string Version, string OperacionId);
public sealed record CancelarOrdenRequest(string Motivo, string OperacionId);
public sealed record DevolucionOperativaDto(int Id, int OrdenId, int ParadaId, int PilotoId,
    string Tipo, string Estado, string Motivo, string? NotaDecision, DateTime SolicitadaAt,
    DateTime? AutorizadaAt, DateTime? IniciadaAt, DateTime? RecibidaAt, int TiendaOrigenId,
    string TiendaOrigenNombre, string? TiendaOrigenDireccion, string? TiendaOrigenTelefono,
    string EstadoParada, string Version, string? ObservacionesRecepcion);
