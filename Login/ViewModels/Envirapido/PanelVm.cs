using Microsoft.AspNetCore.Mvc.Rendering;
using Login.Application.Operations;

namespace Login.ViewModels.Envirapido;

public sealed class PanelVm
{
    public string Empresa { get; set; } = "";
    public string Seccion { get; set; } = "Operaciones";
    public string Tab { get; set; } = "Monitoreo";
    public string? Buscar { get; set; }
    public string? Estado { get; set; }
    public DateTime? Fecha { get; set; }
    public int? ComercioId { get; set; }
    public int? RepartidorId { get; set; }
    public int? ZonaId { get; set; }
    public int Pagina { get; set; } = 1;
    public int PorPagina { get; set; } = 10;
    public string Orden { get; set; } = "recientes";
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling((double)Total / PorPagina));
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
    public List<SelectListItem> Comercios { get; set; } = [];
    public List<SelectListItem> Repartidores { get; set; } = [];
    public Dictionary<int,string> Zonas { get; set; } = [];
    public Dictionary<string,int> ConteosEstado { get; set; } = [];
    public List<PanelOrderVm> Pedidos { get; set; } = [];
    public List<PanelOrderVm> Cola { get; set; } = [];
    public List<PanelPilotVm> Flota { get; set; } = [];
    public List<PanelEventVm> Eventos { get; set; } = [];
    public PanelOrderVm? Pedido { get; set; }
    public int Hoy { get; set; }
    public int PorAsignar { get; set; }
    public int Recogiendo { get; set; }
    public int EnRuta { get; set; }
    public int Entregados { get; set; }
    public int Incidencias { get; set; }
    public int PedidosConIncidencias { get; set; }
    public int RepartidoresActivos { get; set; }
    public int ConGpsVigente { get; set; }
    public int EntregasHoy { get; set; }
    public int? ParadaId { get; set; }
    public OperationsSnapshot? Datos { get; set; }
    public decimal? Puntualidad { get; set; }
    public int PedidosConCompromiso { get; set; }
    public int Pendientes { get; set; }
    public bool Demorados { get; set; }
    public List<DevolucionOperativaDto> Devoluciones { get; set; } = [];
    public RecorridoEstimado? Seguimiento { get; set; }
}

public sealed class PanelOrderVm
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Comercio { get; set; } = "";
    public int ComercioId { get; set; }
    public string RecogidaDireccion { get; set; } = "";
    public string RecogidaTelefono { get; set; } = "";
    public double? RecogidaLatitud { get; set; }
    public double? RecogidaLongitud { get; set; }
    public string Destinatario { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Telefono { get; set; } = "";
    public double? DestinoLatitud { get; set; }
    public double? DestinoLongitud { get; set; }
    public string Estado { get; set; } = "";
    public int? PilotoId { get; set; }
    public string? Piloto { get; set; }
    public string? PilotoTelefono { get; set; }
    public string? Nota { get; set; }
    public bool Auto { get; set; }
    public DateTime CreadoUtc { get; set; }
    public DateTime? AsignadoUtc { get; set; }
    public DateTime? RecogidoUtc { get; set; }
    public DateTime? EntregadoUtc { get; set; }
    public decimal? Precio { get; set; }
    public double? DistanciaKm { get; set; }
    public List<PanelStopVm> Paradas { get; set; } = [];
    public DatosEnvioDto? Datos { get; set; }
    public bool EsTerminal => Estado is "Entregada" or "Devuelta" or "FinalizadaConDevoluciones" or "Cancelada";
    public bool PendienteProgramada => !EsTerminal&&Datos?.ProgramadaPara>DateTime.UtcNow;
    public bool Demorada => !EsTerminal&&Datos?.CompromisoEntrega is DateTime compromiso && compromiso < DateTime.UtcNow && Datos.CanceladaAt == null;
    public bool PuedeAsignar => Estado=="Creada"&&(Datos?.ProgramadaPara==null||Datos.ProgramadaPara<=DateTime.UtcNow);
    public bool PuedeCancelar => !RecogidoUtc.HasValue&&Estado is "Creada" or "Asignada";
    public string EtiquetaEstado => PendienteProgramada?"Pendiente":Estado switch { "Creada" => "Por asignar", "Asignada" => "Recogiendo", "Recolectada" => "En ruta", "Entregada" => "Entregado", "Incidente" => "Con incidencia", "FinalizadaConDevoluciones"=>"Cerrada con devoluciones", "Devuelta"=>"Devuelta", _ => Estado };
    public string ClaseEstado => PendienteProgramada?"gray":Estado switch { "Creada" => "amber", "Asignada" => "blue", "Recolectada" => "teal", "Entregada" => "green", "Incidente" => "red", _ => "gray" };
}

public sealed class PanelStopVm
{
    public int Id { get; set; }
    public int Posicion { get; set; }
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string? Instrucciones { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public string Estado { get; set; } = "";
    public DateTime? EntregadaUtc { get; set; }
}

public sealed class PanelPilotVm
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Telefono { get; set; } = "";
    public bool Activo { get; set; }
    public bool EnLinea { get; set; }
    public int Carga { get; set; }
    public bool ConIncidencia { get; set; }
    public int GpsFrescuraSegundos { get; set; } = 120;
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public DateTime? GpsUtc { get; set; }
    public bool GpsVigente => GpsUtc.HasValue && GpsUtc.Value >= DateTime.UtcNow.AddSeconds(-GpsFrescuraSegundos) && Latitud.HasValue && Longitud.HasValue;
    public string Estado => !Activo ? "Inactivo" : Carga > 0 ? "En entrega" : EnLinea ? "Disponible" : "Pausado";
}

public sealed class PanelEventVm
{
    public int OrdenId { get; set; }
    public DateTime FechaUtc { get; set; }
    public string Tipo { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Descripcion { get; set; } = "";
}
