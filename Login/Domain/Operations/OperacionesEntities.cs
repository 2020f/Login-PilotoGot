using System.ComponentModel.DataAnnotations;
using Login.Domain.Entities;

namespace Login.Domain.Operations;

public abstract class OperativoTenant
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public ClienteApp ClienteApp { get; set; } = null!;
    [MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ZonaOperativa : OperativoTenant
{
    [MaxLength(100)] public string Nombre { get; set; } = "";
    [MaxLength(100)] public string Region { get; set; } = "";
    [MaxLength(20)] public string Color { get; set; } = "#008b89";
    public string PoligonoJson { get; set; } = "[]";
    public double SuperficieKm2 { get; set; }
    public bool Activa { get; set; } = true;
    [MaxLength(5)] public string HoraInicio { get; set; } = "07:00";
    [MaxLength(5)] public string HoraFin { get; set; } = "22:00";
    public int Capacidad { get; set; } = 100;
    [MaxLength(30)] public string UnidadCapacidad { get; set; } = "ParadasPendientes";
    public int AvisoPorcentaje { get; set; } = 90;
}

public enum EstadoRutaOperativa { Planificada = 1, Activa = 2, Finalizada = 3, Cancelada = 4 }
public enum TipoPuntoRuta { Recogida = 1, Entrega = 2, Devolucion = 3 }
public class RutaOperativa : OperativoTenant
{
    [MaxLength(120)] public string Nombre { get; set; } = "";
    public DateOnly Fecha { get; set; }
    public int? ZonaId { get; set; }
    public ZonaOperativa? Zona { get; set; }
    public int? PilotoId { get; set; }
    public Piloto? Piloto { get; set; }
    public EstadoRutaOperativa Estado { get; set; } = EstadoRutaOperativa.Planificada;
    public bool PermitirReordenarPiloto { get; set; }
    public DateTime? IniciadaAt { get; set; }
    public DateTime? FinalizadaAt { get; set; }
    public ICollection<RutaOrdenOperativa> Ordenes { get; set; } = new List<RutaOrdenOperativa>();
    public ICollection<PuntoRutaOperativa> Puntos { get; set; } = new List<PuntoRutaOperativa>();
}
public class RutaOrdenOperativa
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public int RutaId { get; set; }
    public RutaOperativa Ruta { get; set; } = null!;
    public int OrdenEntregaId { get; set; }
    public OrdenEntrega OrdenEntrega { get; set; } = null!;
    public bool Vigente { get; set; } = true;
}
public class PuntoRutaOperativa
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public int RutaId { get; set; }
    public RutaOperativa Ruta { get; set; } = null!;
    public int OrdenEntregaId { get; set; }
    public int RutaOrdenId { get; set; }
    public RutaOrdenOperativa RutaOrden { get; set; } = null!;
    public int? ParadaEntregaId { get; set; }
    public ParadaEntrega? ParadaEntrega { get; set; }
    public TipoPuntoRuta Tipo { get; set; }
    public int Posicion { get; set; }
    public DateTime? LlegadaAt { get; set; }
}

public enum EfectoIncidencia { Continuar = 1, PausarParada = 2, PausarRuta = 3 }
public enum EstadoIncidencia { Nueva = 1, EnAtencion = 2, Escalada = 3, Resuelta = 4 }
public class ReglaIncidenciaOperativa : OperativoTenant
{
    [MaxLength(80)] public string Categoria { get; set; } = "";
    public int Prioridad { get; set; } = 2;
    public int? MinutosResolucion { get; set; }
    public EfectoIncidencia Efecto { get; set; } = EfectoIncidencia.Continuar;
    public bool Activa { get; set; } = true;
    public bool DetectarDemora { get; set; }
}
public class IncidenciaOperativa : OperativoTenant
{
    public int? OrdenEntregaId { get; set; }
    public OrdenEntrega? OrdenEntrega { get; set; }
    public int? ParadaEntregaId { get; set; }
    public ParadaEntrega? ParadaEntrega { get; set; }
    public int? RutaId { get; set; }
    public RutaOperativa? Ruta { get; set; }
    [MaxLength(80)] public string Categoria { get; set; } = "General";
    [MaxLength(1000)] public string Descripcion { get; set; } = "";
    public int Prioridad { get; set; } = 2;
    public EstadoIncidencia Estado { get; set; } = EstadoIncidencia.Nueva;
    public EfectoIncidencia Efecto { get; set; } = EfectoIncidencia.Continuar;
    [MaxLength(450)] public string ReportadaPor { get; set; } = "";
    [MaxLength(450)] public string? ResponsableId { get; set; }
    public DateTime? VenceAt { get; set; }
    public DateTime? ResueltaAt { get; set; }
    [MaxLength(1000)] public string? Resolucion { get; set; }
    [MaxLength(32)] public string? ReglaVersion { get; set; }
    [MaxLength(150)] public string? ClaveDeteccion { get; set; }
    public ICollection<ActividadIncidenciaOperativa> Actividad { get; set; } = new List<ActividadIncidenciaOperativa>();
}
public class ActividadIncidenciaOperativa
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public int IncidenciaId { get; set; }
    public IncidenciaOperativa Incidencia { get; set; } = null!;
    [MaxLength(450)] public string ActorId { get; set; } = "";
    [MaxLength(40)] public string Accion { get; set; } = "";
    [MaxLength(1500)] public string Texto { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public enum UnidadRemuneracion { Servicio = 1, Ruta = 2, Parada = 3 }
public class ReglaRemuneracionOperativa : OperativoTenant
{
    [MaxLength(100)] public string Nombre { get; set; } = "";
    public UnidadRemuneracion Unidad { get; set; }
    public decimal ImporteFijo { get; set; }
    public decimal PorcentajeTarifa { get; set; }
    public decimal IncentivoFijo { get; set; }
    public decimal ComisionPorcentaje { get; set; }
    public bool Activa { get; set; } = true;
}
public enum EstadoLiquidacion { EnRevision = 1, Aprobada = 2, Programada = 3, Pagada = 4 }
public class LiquidacionOperativa : OperativoTenant
{
    public int? PilotoId { get; set; }
    public Piloto? Piloto { get; set; }
    public int? TiendaId { get; set; }
    public Tienda? Tienda { get; set; }
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public EstadoLiquidacion Estado { get; set; } = EstadoLiquidacion.EnRevision;
    [MaxLength(3)] public string Moneda { get; set; } = "DOP";
    public decimal Servicios { get; set; }
    public decimal Incentivos { get; set; }
    public decimal Comisiones { get; set; }
    public decimal Ajustes { get; set; }
    public decimal EfectivoCompensado { get; set; }
    public decimal Neto { get; set; }
    public int Entregas { get; set; }
    public bool Conciliada { get; set; }
    [MaxLength(40)] public string? MetodoPago { get; set; }
    [MaxLength(4)] public string? CuentaUltimos4 { get; set; }
    public DateOnly? FechaProgramada { get; set; }
    public DateTime? PagadaAt { get; set; }
    public DateOnly? FechaPago { get; set; }
    [MaxLength(120)] public string? ReferenciaPago { get; set; }
    [MaxLength(450)] public string? AprobadaPor { get; set; }
    public ICollection<MovimientoOperativo> Movimientos { get; set; } = new List<MovimientoOperativo>();
}
public class MovimientoOperativo : OperativoTenant
{
    public int LiquidacionId { get; set; }
    public LiquidacionOperativa Liquidacion { get; set; } = null!;
    public int? OrdenEntregaId { get; set; }
    public int? ParadaEntregaId { get; set; }
    public int? RutaId { get; set; }
    [MaxLength(40)] public string Concepto { get; set; } = "";
    [MaxLength(200)] public string Descripcion { get; set; } = "";
    [MaxLength(200)] public string ClaveOrigen { get; set; } = "";
    public decimal Importe { get; set; }
    public int? ReglaId { get; set; }
    [MaxLength(32)] public string? ReglaVersion { get; set; }
    public string BasesJson { get; set; } = "{}";
    [MaxLength(450)] public string ActorId { get; set; } = "";
}
public class CobroOperativo : OperativoTenant
{
    public int OrdenEntregaId { get; set; }
    public OrdenEntrega OrdenEntrega { get; set; } = null!;
    public int? ParadaEntregaId { get; set; }
    public int PilotoId { get; set; }
    public Piloto Piloto { get; set; } = null!;
    public int TiendaId { get; set; }
    public Tienda Tienda { get; set; } = null!;
    public decimal Importe { get; set; }
    public decimal? ImporteVerificado { get; set; }
    [MaxLength(80)] public string Referencia { get; set; } = "";
    [MaxLength(500)] public string? Nota { get; set; }
    public DateTime? ConciliadoAt { get; set; }
    public DateTime? EntregadoEmpresaAt { get; set; }
    public decimal? ImporteEntregadoEmpresa { get; set; }
    [MaxLength(120)] public string? ReferenciaEntregaEmpresa { get; set; }
    [MaxLength(450)] public string? RecibidoEmpresaPor { get; set; }
    public int? LiquidacionId { get; set; }
}

public class DatosEnvioOperativos : OperativoTenant
{
    public int OrdenEntregaId { get; set; }
    public OrdenEntrega OrdenEntrega { get; set; } = null!;
    [MaxLength(100)] public string? Referencia { get; set; }
    [MaxLength(60)] public string Servicio { get; set; } = "Estandar";
    public int Paquetes { get; set; } = 1;
    public decimal? PesoKg { get; set; }
    public int Prioridad { get; set; } = 2;
    public int? ZonaId { get; set; }
    public DateTime? ProgramadaPara { get; set; }
    public DateTime? CompromisoEntrega { get; set; }
    public DateTime? CanceladaAt { get; set; }
    [MaxLength(500)] public string? MotivoCancelacion { get; set; }
    [MaxLength(1000)] public string? NotasOperativas { get; set; }
}
public class OperacionEnvirapido
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    [MaxLength(450)] public string ActorId { get; set; } = "";
    [MaxLength(60)] public string Tipo { get; set; } = "";
    [MaxLength(32)] public string Clave { get; set; } = "";
    [MaxLength(64)] public string PayloadHash { get; set; } = "";
    public string ResultadoJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}
public class EventoOperativo
{
    public long Id { get; set; }
    public int ClienteAppId { get; set; }
    [MaxLength(450)] public string ActorId { get; set; } = "";
    [MaxLength(40)] public string Tipo { get; set; } = "";
    public int RecursoId { get; set; }
    [MaxLength(1000)] public string Descripcion { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class PosicionPilotoOperativa
{
    public long Id { get; set; }
    public int ClienteAppId { get; set; }
    public int PilotoId { get; set; }
    public Piloto Piloto { get; set; } = null!;
    [MaxLength(32)] public string DispositivoId { get; set; } = "";
    public long Secuencia { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double? Precision { get; set; }
    public DateTime CapturadoAt { get; set; }
    public DateTime RecibidoAt { get; set; }
}
