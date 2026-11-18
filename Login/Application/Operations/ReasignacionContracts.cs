namespace Login.Application.Operations;

public sealed record RevisionParadaRequest(int ParadaId, string RevisionAsignacion);
public sealed record SolicitarRetiroParadasRequest(int OrdenId, List<RevisionParadaRequest> Paradas,
    string Motivo, string OperacionId, int? IncidenciaId = null);
public sealed record DecidirRetiroParadaRequest(string Accion, string Nota, string Version, string OperacionId);
public sealed record AsignarParadasRequest(int OrdenId, List<RevisionParadaRequest> Paradas, int PilotoId,
    string Motivo, string OperacionId);
public sealed record ProgramarRetornoParadaRequest(string Version, string OperacionId, int? TiendaId = null,
    string? NombreCentro = null, string? DireccionCentro = null, double? Latitud = null, double? Longitud = null);
public sealed record ConfirmarRecepcionParadaRequest(string Version, string Observaciones, string OperacionId);
public sealed record CancelarParadasRequest(int OrdenId, List<RevisionParadaRequest> Paradas,
    string Motivo, string OperacionId);
public sealed record RetiroParadaDto(int Id, int OrdenId, int ParadaId, string CodigoA, string Estado,
    string Motivo, string? NotaDecision, int? PilotoOrigenId, int? PilotoDestinoId, int? PilotoCustodioId,
    string? TipoDestino, int? TiendaDestinoId, string? DestinoNombre, string? DestinoDireccion,
    double? DestinoLatitud, double? DestinoLongitud, DateTime CreatedAt, DateTime? RecibidaAt,
    bool PuedeConfirmarRecepcion, string RevisionAsignacion, string Version, bool Abierta,
    string? PilotoOrigenNombre = null, string? PilotoOrigenTelefono = null,
    string? PilotoDestinoNombre = null, string? PilotoDestinoTelefono = null);
public sealed record ParadaReasignacionDto(int OrdenId, int ParadaId, string CodigoA, string Estado,
    int Posicion, string Destinatario, int? PilotoAsignadoId, int? PilotoCustodioId, bool EnReasignacion,
    DateTime? RecogidaAt, string RevisionAsignacion);
public sealed record ReasignacionSnapshot(List<ParadaReasignacionDto> Paradas, List<RetiroParadaDto> Gestiones);
