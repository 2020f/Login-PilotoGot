namespace Login.Application.Operations;

public sealed record OperationsActor(int TenantId, string UserId, int? PilotoId = null)
{
    public bool EsPiloto => PilotoId.HasValue;
}
public sealed record GeoPoint(double Latitud, double Longitud);
public sealed record GuardarZonaRequest(string Nombre, string Region, string Color, List<GeoPoint> Poligono,
    int Capacidad, string UnidadCapacidad, int AvisoPorcentaje, string HoraInicio, string HoraFin, bool Activa,
    string OperacionId, int? Id = null, string? Version = null);
public sealed record CrearRutaRequest(string Nombre, DateOnly Fecha, List<int> OrdenIds, int? PilotoId,
    int? ZonaId, bool PermitirReordenarPiloto, string OperacionId);
public sealed record ReordenarRutaRequest(List<int> PuntoIds, string Version, string OperacionId);
public sealed record AsignarRutaRequest(int PilotoId, string Version, string OperacionId);
public sealed record EstadoRutaRequest(string Accion, string Version, string OperacionId);
public sealed record LlegadaPuntoRequest(string OperacionId);
public sealed record ReportarIncidenciaRequest(string Categoria, string Descripcion, string OperacionId,
    int? OrdenId = null, int? ParadaId = null, int? RutaId = null);
public sealed record CambiarIncidenciaRequest(string Accion, string Texto, string Version, string OperacionId,
    string? ResponsableId = null);
public sealed record GuardarReglaIncidenciaRequest(string Categoria, int Prioridad, int? MinutosResolucion,
    string Efecto, bool Activa, bool DetectarDemora, string OperacionId, int? Id = null, string? Version = null);
public sealed record GuardarReglaRemuneracionRequest(string Nombre, string Unidad, decimal ImporteFijo,
    decimal PorcentajeTarifa, decimal IncentivoFijo, decimal ComisionPorcentaje, bool Activa,
    string OperacionId, int? Id = null, string? Version = null);
public sealed record GenerarLiquidacionRequest(DateOnly Desde, DateOnly Hasta, string OperacionId,
    int? PilotoId = null, int? TiendaId = null, int? ReglaId = null, List<int>? CobroIds = null);
public sealed record CambiarLiquidacionRequest(string Accion, string Version, string OperacionId,
    string? Metodo = null, string? CuentaUltimos4 = null, DateOnly? Fecha = null, string? Referencia = null,
    string? Nota = null);
public sealed record AjustarLiquidacionRequest(decimal Importe, string Motivo, string Version, string OperacionId);
public sealed record RegistrarCobroRequest(int OrdenId, decimal Importe, string Referencia, string OperacionId,
    int? ParadaId = null, string? Nota = null);
public sealed record ConciliarCobroRequest(decimal ImporteVerificado, string Nota, string Version, string OperacionId);
public sealed record RecibirEfectivoRequest(decimal Importe, string Referencia, string Version, string OperacionId);
public sealed record GuardarDatosEnvioRequest(string? Referencia, string Servicio, int Paquetes, decimal? PesoKg,
    int Prioridad, int? ZonaId, DateTime? ProgramadaPara, DateTime? CompromisoEntrega,
    string? NotasOperativas, string OperacionId, string? Version = null, decimal? PrecioComercial = null);
public sealed record MuestraGps(double Latitud, double Longitud, DateTime CapturadoAt, long Secuencia,
    double? Precision = null);
public sealed record EnviarGpsRequest(string DispositivoId, List<MuestraGps> Muestras, string OperacionId);

public sealed record ZonaOperativaDto(int Id, string Codigo, string Nombre, string Region, string Color,
    List<GeoPoint> Poligono, double SuperficieKm2, bool Activa, string HoraInicio, string HoraFin,
    int Capacidad, string UnidadCapacidad, int AvisoPorcentaje, int Carga, int PedidosSinAsignar,
    int PilotosDisponibles, bool AltaCarga, string Version);
public sealed record PuntoRutaDto(int Id, int OrdenId, int? ParadaId, string Tipo, int Posicion,
    string Nombre, string? Direccion, double? Latitud, double? Longitud, bool Completado,
    bool PuedeVerDestino, DateTime? LlegadaAt, bool ExcluidoPorRetorno = false, string? EstadoEntrega = null,
    bool ExcluidoPorCancelacion = false);
public sealed record RutaOperativaDto(int Id, string Codigo, string Nombre, DateOnly Fecha, int? ZonaId,
    string? Zona, int? PilotoId, string? Piloto, string Estado, bool PermitirReordenarPiloto,
    int RecogidasCompletadas, int TotalRecogidas, int EntregasCompletadas, int TotalEntregas,
    double? DistanciaKm, double? MinutosRestantes, DateTime? LlegadaEstimadaAt, string? Geometria,
    DateTime? EstimacionAt, List<int> OrdenIds, List<PuntoRutaDto> Puntos, string Version, int EntregasDevueltas = 0);
public sealed record ActividadIncidenciaDto(int Id, string Actor, string Accion, string Texto, DateTime CreatedAt);
public sealed record IncidenciaOperativaDto(int Id, string Codigo, int? OrdenId, int? ParadaId, int? RutaId,
    string Categoria, string Descripcion, int Prioridad, string Estado, string Efecto, string? ResponsableId,
    string? Responsable, DateTime? VenceAt, DateTime? ResueltaAt, string? Resolucion,
    bool FueraPlazo, DateTime CreatedAt, List<ActividadIncidenciaDto> Actividad, string Version);
public sealed record ReglaIncidenciaDto(int Id, string Categoria, int Prioridad, int? MinutosResolucion,
    string Efecto, bool Activa, bool DetectarDemora, string Version);
public sealed record ReglaRemuneracionDto(int Id, string Nombre, string Unidad, decimal ImporteFijo,
    decimal PorcentajeTarifa, decimal IncentivoFijo, decimal ComisionPorcentaje, bool Activa, string Version);
public sealed record MovimientoOperativoDto(int Id, string Concepto, string Descripcion, decimal Importe,
    int? OrdenId, int? ParadaId, int? RutaId, DateTime CreatedAt);
public sealed record LiquidacionOperativaDto(int Id, string Codigo, int? PilotoId, int? TiendaId,
    string Beneficiario, DateOnly Desde, DateOnly Hasta, string Estado, string Moneda, int Entregas,
    decimal Servicios, decimal Incentivos, decimal Comisiones, decimal Ajustes, decimal EfectivoCompensado,
    decimal Neto, bool Conciliada, string? MetodoPago, string? CuentaDestino, DateOnly? FechaProgramada,
    DateTime? PagadaAt, string? ReferenciaPago, List<MovimientoOperativoDto> Movimientos, string Version,
    DateOnly? FechaPago = null);
public sealed record CobroOperativoDto(int Id, int OrdenId, int? ParadaId, int PilotoId, int TiendaId,
    decimal Importe, decimal? ImporteVerificado, string Referencia, string? Nota, DateTime? ConciliadoAt,
    int? LiquidacionId, DateTime CreatedAt, string Version, DateTime? EntregadoEmpresaAt = null,
    decimal? ImporteEntregadoEmpresa = null, string? ReferenciaEntregaEmpresa = null);
public sealed record DatosEnvioDto(int OrdenId, string? Referencia, string Servicio, int Paquetes, decimal? PesoKg,
    int Prioridad, int? ZonaId, DateTime? ProgramadaPara, DateTime? CompromisoEntrega, DateTime? CanceladaAt,
    string? NotasOperativas, string Version);
public sealed record OperationsSnapshot(DateTime ActualizadoAt, bool RequiereGps, List<ZonaOperativaDto> Zonas,
    List<RutaOperativaDto> Rutas, List<IncidenciaOperativaDto> Incidencias,
    List<ReglaIncidenciaDto> ReglasIncidencia, List<ReglaRemuneracionDto> ReglasRemuneracion,
    List<LiquidacionOperativaDto> Liquidaciones, List<CobroOperativoDto> Cobros,
    List<DatosEnvioDto> DatosEnvios, List<ActorOperativoDto> Responsables,
    DateTime? AutorizacionGpsHasta = null, int GpsAutorizacionSegundos = 120, List<EventoOperativoDto>? Eventos = null,
    int GpsFrescuraSegundos = 120, CoberturaSnapshot? Cobertura = null, IndicadoresOperativos? Indicadores = null);
public sealed record CoberturaSnapshot(int RutasMostradas, int TotalRutas, int IncidenciasMostradas, int TotalIncidencias,
    int LiquidacionesMostradas, int TotalLiquidaciones, int CobrosMostrados, int TotalCobros);
public sealed record ActorOperativoDto(string Id, string Nombre);
public sealed record OperationsActivity(DateTime ActualizadoAt, bool RequiereGps, DateTime? AutorizacionGpsHasta,
    int GpsAutorizacionSegundos);
public sealed record EventoOperativoDto(long Id, string Actor, string Tipo, int RecursoId, string Descripcion, DateTime CreatedAt);

public sealed class OperationsException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
