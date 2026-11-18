using System.Globalization;
using System.Security.Claims;
using System.Text;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Login.ViewModels.Envirapido;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Login.Controllers;

[Authorize(Roles = "Gestor")]
[Route("Operaciones")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class EnvirapidoController(ApplicationDbContext db, IOrdenService ordenes, EnvirapidoOperationsService operations,IConfiguration configuration,ILogger<EnvirapidoController> logger) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private int GpsFrescuraSegundos=>OperationsGpsPolicy.FreshnessSeconds(configuration);

    private async Task<ClienteApp?> TenantAsync() => await db.ClientesApp.AsNoTracking()
        .SingleOrDefaultAsync(t => t.GestorIdentityUserId == Actor&&t.Estado==EstadoClienteApp.Activo);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? tab, string? buscar, string? estado, DateTime? fecha, int? comercioId, int? repartidorId, int pagina = 1, int porPagina = 10, string orden = "recientes")
    {
        var tenant = await TenantAsync(); if (tenant == null) return Forbid();
        var vm = await BuildAsync(tenant, "Operaciones", tab ?? "Monitoreo", buscar, estado, fecha, comercioId, repartidorId, pagina, porPagina, orden);
        await EnrichAsync(vm);
        return View("Index", vm);
    }

    [HttpGet("Pedidos")]
    public async Task<IActionResult> Pedidos(string? tab, string? buscar, string? estado, DateTime? fecha, int? comercioId, int? repartidorId, int pagina = 1, int porPagina = 10, string orden = "recientes")
    {
        var tenant = await TenantAsync(); if (tenant == null) return Forbid();
        var vm=await BuildAsync(tenant, "Pedidos", tab ?? "Listado", buscar, estado, fecha, comercioId, repartidorId, pagina, porPagina, orden);await EnrichAsync(vm);return View(vm);
    }

    [HttpGet("Buscar")]
    public async Task<IActionResult> Buscar(string? buscar)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();buscar=buscar?.Trim();if(buscar?.Length>100)buscar=buscar[..100];
        var vm=await BuildAsync(tenant,"Operaciones","Búsqueda",buscar,null,null,null,null,1,25,"recientes");
        if(!string.IsNullOrWhiteSpace(buscar)){vm.Flota=vm.Flota.Where(p=>p.Nombre.Contains(buscar,StringComparison.OrdinalIgnoreCase)||p.Telefono.Contains(buscar,StringComparison.OrdinalIgnoreCase)).ToList();vm.Comercios=vm.Comercios.Where(t=>t.Text.Contains(buscar,StringComparison.OrdinalIgnoreCase)).ToList();}
        else{vm.Flota=[];vm.Comercios=[];vm.Pedidos=[];vm.Total=0;}
        return View(vm);
    }

    [HttpGet("BuscarPedidosRuta")]
    public async Task<IActionResult> BuscarPedidosRuta(string? buscar,int? pilotoId=null)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();if(buscar?.Length>100)buscar=buscar[..100];
        if(pilotoId.HasValue&&!await db.Pilotos.AnyAsync(p=>p.Id==pilotoId&&p.ClienteAppId==tenant.Id&&p.Activo))return NotFound();
        var rows=await Project(Filter(CandidateOrders(tenant.Id,pilotoId),buscar,null,null,null,null).OrderByDescending(o=>o.Id).Take(100)).ToListAsync();await AttachOrderDataAsync(rows,tenant.Id);return Json(rows);
    }

    private IQueryable<OrdenEntrega> CandidateOrders(int tenantId,int? pilotId)=>db.OrdenesEntrega.AsNoTracking().Where(o=>o.ClienteAppId==tenantId&&!o.GestionPorParadas&&(o.Estado==EstadoOrden.Creada||o.Estado==EstadoOrden.Asignada)&&(!o.PilotoId.HasValue||o.PilotoId==pilotId)
        &&!db.Set<RutaOrdenOperativa>().Any(r=>r.ClienteAppId==tenantId&&r.OrdenEntregaId==o.Id&&r.Vigente));

    [HttpGet("Fragmento")]
    public async Task<IActionResult> Fragmento(string? seccion, string? tab, string? buscar, string? estado, DateTime? fecha, int? comercioId, int? repartidorId, int pagina = 1, int porPagina = 10, string orden = "recientes")
    {
        var tenant = await TenantAsync(); if (tenant == null) return Forbid();
        var vm = await BuildAsync(tenant, seccion == "Pedidos" ? "Pedidos" : "Operaciones", tab ?? "Listado", buscar, estado, fecha, comercioId, repartidorId, pagina, porPagina, orden);
        return PartialView("_PedidosTabla", vm);
    }

    [HttpGet("Pedido/{id:int}")]
    public async Task<IActionResult> Pedido(int id, string tab = "Resumen", int? paradaId = null)
    {
        var tenant = await TenantAsync(); if (tenant == null) return Forbid();
        var order = await Project(db.OrdenesEntrega.AsNoTracking().Where(o => o.Id == id && o.ClienteAppId == tenant.Id)).SingleOrDefaultAsync();
        if (order == null) return NotFound();
        MarkUtc(order);
        if (paradaId.HasValue && !order.Paradas.Any(p => p.Id == paradaId)) return NotFound();
        var vm = new PanelVm { Empresa = tenant.NombreComercial, Seccion = "Pedidos", Tab = tab, Pedido = order, ParadaId = paradaId };
        vm.Eventos = await db.HistorialOrdenes.AsNoTracking().Where(h => h.OrdenEntregaId == id && h.OrdenEntrega.ClienteAppId == tenant.Id)
            .OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id)
            .Select(h => new PanelEventVm { OrdenId = h.OrdenEntregaId, FechaUtc = h.CreatedAt, Tipo = h.TipoEvento.ToString(), Actor = h.ActorRol, Descripcion = h.Descripcion }).ToListAsync();
        vm.Flota = await FlotaAsync(tenant.Id);
        await EnrichAsync(vm);
        vm.ParadaId=paradaId??order.Paradas.FirstOrDefault(p=>p.Estado=="Pendiente")?.Id??order.Paradas.FirstOrDefault()?.Id;
        vm.Seguimiento=await operations.SeguimientoPedidoAsync(await operations.ManagerAsync(Actor),id,vm.ParadaId,HttpContext.RequestAborted);
        return View(vm);
    }

    [HttpGet("MapaDatos")]
    public async Task<IActionResult> MapaDatos()
    {
        var tenant = await TenantAsync(); if (tenant == null) return Forbid();
        var fleet = await FlotaAsync(tenant.Id);
        var snapshot = await operations.SnapshotAsync(await operations.ManagerAsync(Actor));
        return Json(new { actualizadoUtc = DateTime.UtcNow, pilotos = fleet,
            rutas = snapshot.Rutas.Where(r => r.Estado is "Recogiendo" or "EnRuta" or "EnRetorno") });
    }

    [HttpGet("Seguimiento/{id:int}")]
    public async Task<IActionResult> Seguimiento(int id,int? paradaId=null)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();
        var order=await db.OrdenesEntrega.AsNoTracking().Where(o=>o.Id==id&&o.ClienteAppId==tenant.Id).Select(o=>new{o.Estado,o.PilotoId,Paradas=o.Paradas.OrderBy(p=>p.Id).Select(p=>new{p.Id,Estado=p.Estado.ToString()}).ToList()}).SingleOrDefaultAsync();
        if(order==null||paradaId.HasValue&&!order.Paradas.Any(p=>p.Id==paradaId))return NotFound();
        var estimate=await operations.SeguimientoPedidoAsync(await operations.ManagerAsync(Actor),id,paradaId,HttpContext.RequestAborted);
        return Json(new{estado=order.Estado.ToString(),pilotoId=order.PilotoId,paradas=order.Paradas,seguimiento=estimate,actualizadoUtc=DateTime.UtcNow});
    }

    [HttpGet("Seguimientos")]
    public async Task<IActionResult> Seguimientos(string? ids)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();
        if(string.IsNullOrWhiteSpace(ids)||ids.Length>1000)return BadRequest(new{error="Selecciona entre 1 y 50 pedidos para consultar ETA."});
        var values=ids.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(s=>int.TryParse(s,out var n)?n:0).Distinct().ToArray();
        if(values.Length is <1 or >50||values.Any(n=>n<=0))return BadRequest(new{error="Selecciona entre 1 y 50 pedidos válidos."});
        try
        {
            var actor=await operations.ManagerAsync(Actor);var own=await db.OrdenesEntrega.AsNoTracking().Where(o=>o.ClienteAppId==tenant.Id&&values.Contains(o.Id)).Select(o=>new{o.Id,o.Estado}).ToListAsync();
            var estimates=await operations.SeguimientoPedidosAsync(actor,values,HttpContext.RequestAborted);var rows=new List<object>();
            foreach(var order in own){var terminal=EstadosEntrega.Terminales.Contains(order.Estado);var estimate=estimates.GetValueOrDefault(order.Id);var summary=estimate==null?null:new{distanciaKm=estimate.DistanciaKm,minutos=estimate.Minutos,calculadoAt=estimate.CalculadoAt,llegadaUtc=estimate.CalculadoAt.AddMinutes(estimate.Minutos)};rows.Add(new{ordenId=order.Id,estado=order.Estado.ToString(),esTerminal=terminal,seguimiento=summary});}
            return Json(new{actualizadoUtc=DateTime.UtcNow,pedidos=rows});
        }
        catch(OperationCanceledException)when(HttpContext.RequestAborted.IsCancellationRequested){return new EmptyResult();}
        catch(OperationsException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
        catch(Exception ex){logger.LogError(ex,"No se pudo consultar ETA del panel. TraceId {TraceId}",HttpContext.TraceIdentifier);return StatusCode(500,new{error="No se pudo actualizar la estimación. Se reintentará automáticamente."});}
    }

    [HttpPost("Asignar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Asignar(int ordenId, int pilotoId, string? volver)
    {
        var tenant = await TenantAsync(); if (tenant == null) return Forbid();
        var code = await db.CodigosEntrega.AsNoTracking().Where(c => c.OrdenEntregaId == ordenId && c.OrdenEntrega.ClienteAppId == tenant.Id && c.Tipo == TipoCodigo.B_Recoleccion)
            .Select(c => c.Codigo).SingleOrDefaultAsync();
        if (code == null) return NotFound();
        try { await ordenes.AsignarPilotoAsync(code, pilotoId, Actor, "Gestor", ordenId); if (Request.Headers.Accept.ToString().Contains("application/json")) return Json(new {ok = true}); TempData["EnvirapidoOk"] = "Repartidor asignado."; }
        catch (OperationsException ex) { if(Request.Headers.Accept.ToString().Contains("application/json"))return StatusCode(ex.StatusCode,new{ok=false,error=ex.Message});TempData["EnvirapidoError"]=ex.Message; }
        catch (RejectedOperationException ex) { if(Request.Headers.Accept.ToString().Contains("application/json"))return StatusCode(ex.StatusCode,new{ok=false,error=ex.Message,solicitudRechazada=true});TempData["EnvirapidoError"]=ex.Message; }
        catch (InvalidOperationException ex) { if (Request.Headers.Accept.ToString().Contains("application/json")) return BadRequest(new {ok = false,error = ex.Message}); TempData["EnvirapidoError"] = ex.Message; }
        return LocalRedirect(Url.IsLocalUrl(volver) ? volver! : Url.Action(nameof(Index))!);
    }

    [HttpGet("Resumen")]
    public async Task<IActionResult> Resumen() { var tenant = await TenantAsync(); if (tenant == null) return Forbid(); return View("Index", await BuildAsync(tenant, "Resumen", "Monitoreo", null, null, null, null, null, 1, 10, "recientes")); }

    [HttpGet("Rutas")]
    public Task<IActionResult> Rutas(string tab = "Vista general", int? id = null, string? buscar = null) => ModuleAsync("Rutas y zonas",tab,id,buscar);
    [HttpGet("Incidencias")]
    public Task<IActionResult> Incidencias(string tab = "Bandeja", int? id = null, int? ordenId = null, int? rutaId = null, int? paradaId = null, string? buscar = null) => ModuleAsync("Incidencias",tab,id,buscar,ordenId,rutaId,paradaId:paradaId);
    [HttpGet("Liquidaciones")]
    public Task<IActionResult> Liquidaciones(string tab = "Repartidores", int? id = null, string? buscar = null) => ModuleAsync("Liquidaciones",tab,id,buscar);
    [HttpGet("Automatizacion")]
    public Task<IActionResult> Automatizacion() => ModuleAsync("Operaciones","Automatización");
    [HttpGet("Programados")]
    public Task<IActionResult> Programados() => ModuleAsync("Pedidos","Programados");
    [HttpGet("Devoluciones")]
    public Task<IActionResult> Devoluciones(int? id=null) => ModuleAsync("Pedidos","Devoluciones",id);
    [HttpGet("Reportes")]
    public Task<IActionResult> Reportes() => ModuleAsync("Reportes","Resumen");
    [HttpGet("Configuracion")]
    public Task<IActionResult> Configuracion() => ModuleAsync("Configuración","Configuración");
    [HttpGet("Repartidores")]
    public IActionResult Repartidores() => RedirectToAction("Pilotos","Supervisor");
    [HttpGet("Comercios")]
    public IActionResult Comercios() => RedirectToAction("Tiendas","Supervisor");
    [HttpGet("Usuarios")]
    public IActionResult Usuarios() => RedirectToAction("UsuariosTienda","Gestor");

    [HttpGet("ModuloFragmento")]
    public Task<IActionResult> ModuloFragmento(string seccion,string tab,int? id=null,string? buscar=null,int? ordenId=null,int? rutaId=null,int? paradaId=null)
    {
        if(!new[]{"Rutas y zonas","Incidencias","Liquidaciones","Operaciones","Pedidos","Reportes","Configuración"}.Contains(seccion))return Task.FromResult<IActionResult>(BadRequest());
        return ModuleAsync(seccion,tab,id,buscar,ordenId,rutaId,true,paradaId);
    }

    private async Task<IActionResult> ModuleAsync(string section, string tab, int? id = null, string? buscar = null, int? ordenId = null, int? rutaId = null,bool fragment=false,int? paradaId=null)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();
        var actor=await operations.ManagerAsync(Actor);
        DateOnly? desde=null,hasta=null;if(DateOnly.TryParse(Request.Query["desde"],out var from))desde=from;if(DateOnly.TryParse(Request.Query["hasta"],out var to))hasta=to;
        DateOnly? dia=null;if(section=="Rutas y zonas"){if(DateOnly.TryParse(Request.Query["fecha"],out var requestedDay))dia=requestedDay;else if(Request.Query["todos"]!="1"&&!id.HasValue)dia=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-4));if(dia.HasValue){desde=dia;hasta=dia;}}
        if(desde.HasValue&&hasta.HasValue&&desde>hasta)return BadRequest("El período no es válido.");
        var snapshot=await operations.SnapshotAsync(actor,desde,hasta);
        if(id.HasValue&&((section=="Rutas y zonas"&&!snapshot.Rutas.Any(r=>r.Id==id))||(section=="Incidencias"&&!snapshot.Incidencias.Any(i=>i.Id==id))||(section=="Liquidaciones"&&!snapshot.Liquidaciones.Any(l=>l.Id==id))))return NotFound();
        if(ordenId.HasValue&&!await db.OrdenesEntrega.AnyAsync(o=>o.Id==ordenId&&o.ClienteAppId==tenant.Id))return NotFound();
        if(paradaId.HasValue&&(!ordenId.HasValue||!await db.Set<ParadaEntrega>().AnyAsync(p=>p.Id==paradaId&&p.OrdenEntregaId==ordenId&&p.ClienteAppId==tenant.Id)))return NotFound();
        if(rutaId.HasValue&&!snapshot.Rutas.Any(r=>r.Id==rutaId))return NotFound();
        if(section=="Liquidaciones"&&id.HasValue&&(tab is "Repartidores" or "Comercios"))tab=snapshot.Liquidaciones.Single(l=>l.Id==id).TiendaId.HasValue?"Comercios":"Repartidores";
        var vm=new OperationsPageVm {Empresa=tenant.NombreComercial,Seccion=section,Tab=tab,Id=id,Buscar=buscar,OrdenId=ordenId,RutaId=rutaId,ParadaId=paradaId,Datos=snapshot,Operacion=await BuildAsync(tenant,section,tab,null,null,null,null,null,1,50,"recientes")};
        if(section=="Pedidos"&&tab=="Devoluciones")vm.Devoluciones=await operations.DevolucionesAsync(actor);
        if(section=="Pedidos"&&tab=="Devoluciones"&&id.HasValue&&!vm.Devoluciones.Any(x=>x.Id==id))return NotFound();
        vm.Desde=desde;vm.Hasta=hasta;
        vm.Dia=dia;
        if(section=="Rutas y zonas"){vm.CandidatasRuta=await Project(CandidateOrders(tenant.Id,null).OrderByDescending(o=>o.Id).Take(100)).ToListAsync();await AttachOrderDataAsync(vm.CandidatasRuta,tenant.Id);}
        var selectedOrder=ordenId??snapshot.Incidencias.FirstOrDefault(i=>i.Id==id)?.OrdenId;
        if(selectedOrder.HasValue&&!vm.Operacion.Pedidos.Any(p=>p.Id==selectedOrder)) {var extra=await Project(db.OrdenesEntrega.AsNoTracking().Where(o=>o.ClienteAppId==tenant.Id&&o.Id==selectedOrder)).SingleOrDefaultAsync();if(extra!=null)vm.Operacion.Pedidos.Add(extra);}
        ViewData["Empresa"]=vm.Empresa;ViewData["Seccion"]=section;
        return fragment?PartialView("_ModuloContenido",vm):View("Modulo",vm);
    }

    [HttpGet("CrearEnvio")]
    public async Task<IActionResult> CrearEnvio(int? comercioId=null)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();
        var vm=new CrearEnvioPanelVm {ComercioId=comercioId??0,Destinatarios=[new()]};await PrepareCreateAsync(vm,tenant);return View(vm);
    }
    [HttpPost("CrearEnvio")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearEnvio(CrearEnvioPanelVm input)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();
        if(input.Destinatarios.Count is <1 or >100)ModelState.AddModelError("Destinatarios","Selecciona entre 1 y 100 destinatarios.");
        if(!await db.Tiendas.AnyAsync(t=>t.Id==input.ComercioId&&t.ClienteAppId==tenant.Id&&t.Activo))return Forbid();
        if(ModelState.IsValid)
        {
            try {var result=await ordenes.CrearOrdenAsync(tenant.Id,input.ComercioId,input.Destinatarios[0].UsuarioFinalId,input.Nota,Actor,"Gestor",input.OperacionId,input.Destinatarios.Select(p=>new OrdenDestinatarioRequest(p.UsuarioFinalId,p.Instrucciones)).ToList());TempData["EnvirapidoOk"]="Envío creado.";return RedirectToAction(nameof(Pedido),new{id=result.OrdenId});}
            catch(OperationsException ex){ModelState.AddModelError("",ex.Message);}catch(RejectedOperationException ex){ModelState.AddModelError("",ex.Message);}catch(InvalidOperationException ex){ModelState.AddModelError("",ex.Message);}catch(ArgumentException ex){ModelState.AddModelError("",ex.Message);}
        }
        await PrepareCreateAsync(input,tenant);return View(input);
    }
    private async Task PrepareCreateAsync(CrearEnvioPanelVm vm,ClienteApp tenant)
    {
        ViewData["Empresa"]=tenant.NombreComercial;ViewData["Seccion"]="Pedidos";
        vm.Panel=await BuildAsync(tenant,"Pedidos","Crear envío",null,null,null,null,null,1,10,"recientes");
        vm.Personas=await db.UsuariosFinales.AsNoTracking().Where(p=>p.Activo&&p.Tienda.Activo&&p.Tienda.ClienteAppId==tenant.Id).OrderBy(p=>p.Nombre).Select(p=>new PanelRecipientVm{Id=p.Id,ComercioId=p.TiendaId,Nombre=p.Nombre,Direccion=p.DireccionUbicacion}).ToListAsync();
    }

    [HttpGet("Etiquetas")]
    public async Task<IActionResult> Etiquetas(string? ids)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();var selected=(ids??"").Split(',').Select(s=>int.TryParse(s,out var n)?n:0).Where(n=>n>0).Distinct().Take(101).ToArray();if(selected.Length is <1 or >100)return BadRequest("Selecciona entre 1 y 100 pedidos para imprimir.");
        var list=await Project(db.OrdenesEntrega.AsNoTracking().Where(o=>o.ClienteAppId==tenant.Id&&selected.Contains(o.Id))).ToListAsync();return View(new PanelVm{Empresa=tenant.NombreComercial,Seccion="Pedidos",Pedidos=list});
    }

    [HttpGet("Importaciones")]
    public async Task<IActionResult> Importaciones()
    {var tenant=await TenantAsync();if(tenant==null)return Forbid();var vm=new ImportacionVm();await PrepareImportAsync(vm,tenant);return View(vm);}
    [HttpGet("PlantillaImportacion")]
    public IActionResult PlantillaImportacion()=>File(Encoding.UTF8.GetBytes("ComercioId,DestinatarioIds,Nota\r\n"),"text/csv; charset=utf-8","envirapido-plantilla.csv");

    [HttpPost("PrevisualizarImportacion")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(262144)]
    public async Task<IActionResult> PrevisualizarImportacion(IFormFile? archivo)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();var vm=new ImportacionVm();
        try
        {
            if(archivo==null||archivo.Length==0||archivo.Length>250000)throw new ArgumentException("Selecciona un CSV de hasta 250 KB.");
            using var reader=new StreamReader(archivo.OpenReadStream(),new UTF8Encoding(false,true),true);
            using var parser=new TextFieldParser(reader){HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=true};parser.SetDelimiters(",");
            var header=parser.ReadFields();if(header==null||header.Length!=3||!header.SequenceEqual(new[]{"ComercioId","DestinatarioIds","Nota"},StringComparer.OrdinalIgnoreCase))throw new ArgumentException("La cabecera debe ser ComercioId,DestinatarioIds,Nota. Descarga la plantilla.");
            var line=2;while(!parser.EndOfData){if(vm.Filas.Count>=100)throw new ArgumentException("La importación admite hasta 100 filas por archivo.");var fields=parser.ReadFields();var row=new ImportacionFilaVm{Linea=line++};if(fields==null||fields.Length!=3){row.Error="La fila debe contener tres columnas.";}else{row.ComercioId=int.TryParse(fields[0],out var store)?store:0;row.DestinatarioIds=fields[1];row.Nota=fields[2];}vm.Filas.Add(row);}
            if(vm.Filas.Count==0)throw new ArgumentException("El archivo no contiene filas.");
            await ValidateImportAsync(vm,tenant.Id);
        }
        catch(Exception ex)when(ex is ArgumentException or MalformedLineException or DecoderFallbackException){vm.Error=ex.Message;vm.Filas.Clear();}
        await PrepareImportAsync(vm,tenant);return View("Importaciones",vm);
    }
    [HttpPost("ConfirmarImportacion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarImportacion(ImportacionVm input)
    {
        var tenant=await TenantAsync();if(tenant==null)return Forbid();if(input.Filas.Count is <1 or >100)return BadRequest("La importación debe tener entre 1 y 100 filas.");
        foreach(var row in input.Filas){row.OrdenId=null;row.Codigo=null;row.Error=null;}
        await ValidateImportAsync(input,tenant.Id);
        foreach(var row in input.Filas.Where(r=>r.Error==null))
        {
            try{if(!Guid.TryParse(row.OperacionId,out var key))throw new ArgumentException("El identificador de importación no es válido.");row.OperacionId=key.ToString("N");var ids=ParseRecipientIds(row.DestinatarioIds);var created=await ordenes.CrearOrdenAsync(tenant.Id,row.ComercioId,ids[0],row.Nota,Actor,"Gestor",row.OperacionId,ids.Select(id=>new OrdenDestinatarioRequest(id)).ToList());row.OrdenId=created.OrdenId;row.Codigo=created.CodigoA??created.NumeroOrdenA.ToString();await RegistrarImportacionAsync(tenant.Id,row);}
            catch(Exception ex)when(ex is OperationsException or RejectedOperationException or InvalidOperationException or ArgumentException){row.Error=ex.Message;}
        }
        input.Confirmada=true;await PrepareImportAsync(input,tenant);return View("Importaciones",input);
    }
    private async Task RegistrarImportacionAsync(int tenantId,ImportacionFilaVm row)
    {
        if(await db.OperacionesOrden.AsNoTracking().AnyAsync(o=>o.ClienteAppId==tenantId&&o.ActorIdentityUserId==Actor&&o.Tipo=="ImportarOrden"&&o.Clave==row.OperacionId))return;
        var confirmed=await db.OperacionesOrden.AsNoTracking().SingleAsync(o=>o.ClienteAppId==tenantId&&o.ActorIdentityUserId==Actor&&o.Tipo=="CrearOrden"&&o.Clave==row.OperacionId);
        var marker=new OperacionOrden{ClienteAppId=tenantId,ActorIdentityUserId=Actor,Tipo="ImportarOrden",Clave=row.OperacionId,OrdenEntregaId=confirmed.OrdenEntregaId,PayloadHash=confirmed.PayloadHash,ResultadoJson=System.Text.Json.JsonSerializer.Serialize(new{ordenId=confirmed.OrdenEntregaId,origen="CSV",linea=row.Linea})};db.OperacionesOrden.Add(marker);
        try{await db.SaveChangesAsync();}catch(DbUpdateException){db.Entry(marker).State=EntityState.Detached;if(!await db.OperacionesOrden.AsNoTracking().AnyAsync(o=>o.ClienteAppId==tenantId&&o.ActorIdentityUserId==Actor&&o.Tipo=="ImportarOrden"&&o.Clave==row.OperacionId))throw;}
    }
    private static int[] ParseRecipientIds(string raw)
    {if(string.IsNullOrWhiteSpace(raw)||raw.Length>1200)throw new ArgumentException("DestinatarioIds es obligatorio y admite hasta 1200 caracteres.");var values=raw.Split('|').Select(x=>int.TryParse(x.Trim(),out var n)?n:0).ToArray();if(values.Length is <1 or >100||values.Any(n=>n<=0)||values.Distinct().Count()!=values.Length)throw new ArgumentException("DestinatarioIds debe contener entre 1 y 100 IDs positivos distintos, separados por |.");return values;}
    private async Task ValidateImportAsync(ImportacionVm vm,int tenantId)
    {
        var stores=await db.Tiendas.AsNoTracking().Where(t=>t.ClienteAppId==tenantId&&t.Activo).Select(t=>t.Id).ToListAsync();
        var people=await db.UsuariosFinales.AsNoTracking().Where(p=>p.Activo&&p.Tienda.ClienteAppId==tenantId&&p.Tienda.Activo).Select(p=>new{p.Id,p.TiendaId}).ToDictionaryAsync(p=>p.Id,p=>p.TiendaId);
        foreach(var row in vm.Filas){if(row.Error!=null)continue;try{if(!stores.Contains(row.ComercioId))throw new ArgumentException("El comercio no está activo o no pertenece a tu empresa.");var ids=ParseRecipientIds(row.DestinatarioIds);if(ids.Any(id=>!people.TryGetValue(id,out var store)||store!=row.ComercioId))throw new ArgumentException("Un destinatario no pertenece al comercio o está inactivo.");if(row.Nota?.Length>400)throw new ArgumentException("La nota admite hasta 400 caracteres.");}catch(ArgumentException ex){row.Error=ex.Message;}}
    }
    private async Task PrepareImportAsync(ImportacionVm vm,ClienteApp tenant)
    {
        vm.Empresa=tenant.NombreComercial;ViewData["Empresa"]=vm.Empresa;ViewData["Seccion"]="Pedidos";
        vm.Historial=await db.OperacionesOrden.AsNoTracking().Where(o=>o.ClienteAppId==tenant.Id&&o.ActorIdentityUserId==Actor&&o.Tipo=="ImportarOrden")
            .OrderByDescending(o=>o.CreatedAt).Take(100).Select(o=>new ImportacionHistorialVm{OrdenId=o.OrdenEntregaId,Codigo=o.OrdenEntrega.CodigoA??o.OrdenEntrega.NumeroOrdenA.ToString(),CreadoUtc=o.CreatedAt}).ToListAsync();
    }

    [HttpGet("Exportar")]
    public async Task<IActionResult> Exportar(string? buscar, string? estado, DateTime? fecha, int? comercioId, int? repartidorId, string? ids)
    {
        var tenant = await TenantAsync(); if (tenant == null) return Forbid();
        var query = Filter(db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == tenant.Id), buscar, estado, fecha, comercioId, repartidorId);
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var selected = ids.Split(',').Take(1001).Select(x => int.TryParse(x, out var id) ? id : 0).Where(x => x > 0).Distinct().ToArray();
            if (selected.Length == 0 || selected.Length > 1000) return BadRequest("Selecciona entre 1 y 1000 pedidos.");
            query = query.Where(o => selected.Contains(o.Id));
        }
        var rows = await Project(query.OrderByDescending(o => o.Id).Take(10001)).ToListAsync();
        foreach(var row in rows)MarkUtc(row);
        if (rows.Count > 10000) return BadRequest("Reduce los filtros para exportar hasta 10000 pedidos.");
        static string Cell(string? s) { s ??= ""; if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        var csv = new StringBuilder("Pedido,Comercio,Destinatario,Direccion,Repartidor,Estado,CreadoUTC,PrecioRD\r\n");
        foreach (var r in rows) csv.AppendLine(string.Join(',', new[] { r.Codigo, r.Comercio, r.Destinatario, r.Direccion, r.Piloto, r.EtiquetaEstado, r.CreadoUtc.ToString("O"), r.Precio?.ToString(CultureInfo.InvariantCulture) }.Select(Cell)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"envirapido-pedidos-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private async Task<PanelVm> BuildAsync(ClienteApp tenant, string section, string tab, string? buscar, string? estado, DateTime? fecha, int? comercioId, int? repartidorId, int pagina, int porPagina, string orden)
    {
        var vm = new PanelVm { Empresa = tenant.NombreComercial, Seccion = section, Tab = tab, Buscar = buscar?.Trim(), Estado = estado, Fecha = fecha,
            ComercioId = comercioId, RepartidorId = repartidorId, Pagina = Math.Clamp(pagina, 1, 1000000), PorPagina = new[] { 10, 25, 50 }.Contains(porPagina) ? porPagina : 10, Orden = orden };
        vm.Demorados=Request.Query["demorados"]=="true";
        if(int.TryParse(Request.Query["zonaId"],out var zoneId)&&zoneId>0)vm.ZonaId=zoneId;
        vm.Zonas=await db.Set<ZonaOperativa>().AsNoTracking().Where(z=>z.ClienteAppId==tenant.Id).ToDictionaryAsync(z=>z.Id,z=>z.Nombre);
        vm.Comercios = await db.Tiendas.AsNoTracking().Where(t => t.ClienteAppId == tenant.Id).OrderBy(t => t.Nombre).Select(t => new SelectListItem(t.Nombre, t.Id.ToString())).ToListAsync();
        vm.Repartidores = await db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == tenant.Id && p.Activo).OrderBy(p => p.Nombre).Select(p => new SelectListItem(p.Nombre, p.Id.ToString())).ToListAsync();
        var all = db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == tenant.Id);
        var query = Filter(all, vm.Buscar, estado, fecha, comercioId, repartidorId);
        vm.Total = await query.CountAsync(); vm.Pagina = Math.Min(vm.Pagina, vm.Paginas);
        var sorted = orden switch { "antiguos" => query.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id), "comercio" => query.OrderBy(o => o.RecogidaNombre).ThenByDescending(o => o.Id), _ => query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id) };
        vm.Pedidos = await Project(sorted.Skip((vm.Pagina - 1) * vm.PorPagina).Take(vm.PorPagina)).ToListAsync();
        var scope = Filter(all, null, null, fecha, comercioId, repartidorId);
        var stateScope=Filter(all,vm.Buscar,null,fecha,comercioId,repartidorId);
        var grouped=await stateScope.GroupBy(o=>o.Estado).Select(g=>new{Estado=g.Key,Cantidad=g.Count()}).ToListAsync();
        vm.ConteosEstado=grouped.ToDictionary(g=>g.Estado.ToString(),g=>g.Cantidad);vm.ConteosEstado[""]=await stateScope.CountAsync();vm.ConteosEstado["Creada"]=await Filter(stateScope,null,"Creada",null,null,null).CountAsync();vm.ConteosEstado["Pendientes"]=await Filter(stateScope,null,"Pendientes",null,null,null).CountAsync();vm.ConteosEstado["Incidente"]=await Filter(stateScope,null,"Incidente",null,null,null).CountAsync();
        var dayStart = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-4).Date.AddHours(4), DateTimeKind.Utc);
        vm.Hoy = await all.CountAsync(o => o.CreatedAt >= dayStart && o.CreatedAt < dayStart.AddDays(1));
        vm.EntregasHoy = await db.Set<ParadaEntrega>().CountAsync(p=>p.ClienteAppId==tenant.Id&&p.EntregadaAt>=dayStart&&p.EntregadaAt<dayStart.AddDays(1));
        vm.PorAsignar = await Filter(scope,null,"Creada",null,null,null).CountAsync();
        vm.Pendientes = await Filter(scope,null,"Pendientes",null,null,null).CountAsync();
        vm.Recogiendo = await scope.CountAsync(o => o.Estado == EstadoOrden.Asignada);
        vm.EnRuta = await scope.CountAsync(o => o.Estado == EstadoOrden.Recolectada);
        vm.Entregados = await scope.CountAsync(o => o.Estado == EstadoOrden.Entregada);
        vm.Incidencias = await db.Set<IncidenciaOperativa>().CountAsync(i=>i.ClienteAppId==tenant.Id&&i.Estado!=EstadoIncidencia.Resuelta);
        var cases=db.Set<IncidenciaOperativa>().Where(i=>i.ClienteAppId==tenant.Id&&i.Estado!=EstadoIncidencia.Resuelta&&i.OrdenEntregaId.HasValue).Select(i=>i.OrdenEntregaId!.Value);
        vm.PedidosConIncidencias=await scope.Where(o=>cases.Contains(o.Id)||o.Estado==EstadoOrden.Incidente).CountAsync();
        vm.Cola = await Project(Filter(scope,null,"Creada",null,null,null).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Take(20)).ToListAsync();
        vm.Flota = await FlotaAsync(tenant.Id); vm.RepartidoresActivos = vm.Flota.Count(p => p.Activo && p.EnLinea); vm.ConGpsVigente = vm.Flota.Count(p => p.GpsVigente);
        var commitments=scope.Where(o=>o.Estado==EstadoOrden.Entregada).Join(db.Set<DatosEnvioOperativos>().Where(d=>d.ClienteAppId==tenant.Id&&d.CompromisoEntrega.HasValue),o=>o.Id,d=>d.OrdenEntregaId,(o,d)=>new{o.EntregadaAt,d.CompromisoEntrega});
        vm.PedidosConCompromiso=await commitments.CountAsync();if(vm.PedidosConCompromiso>0)vm.Puntualidad=Math.Round(100m*await commitments.CountAsync(x=>x.EntregadaAt<=x.CompromisoEntrega)/vm.PedidosConCompromiso,1);
        var ids=vm.Pedidos.Concat(vm.Cola).Select(p=>p.Id).Distinct().ToArray();
        var details=await db.Set<DatosEnvioOperativos>().AsNoTracking().Where(d=>d.ClienteAppId==tenant.Id&&ids.Contains(d.OrdenEntregaId)).Select(d=>new DatosEnvioDto(d.OrdenEntregaId,d.Referencia,d.Servicio,d.Paquetes,d.PesoKg,d.Prioridad,d.ZonaId,d.ProgramadaPara,d.CompromisoEntrega,d.CanceladaAt,d.NotasOperativas,d.Version)).ToListAsync();
        foreach(var p in vm.Pedidos.Concat(vm.Cola)){p.Datos=details.FirstOrDefault(d=>d.OrdenId==p.Id);MarkUtc(p);}
        if (tab == "Historial") vm.Eventos = await db.HistorialOrdenes.AsNoTracking().Where(h => h.OrdenEntrega.ClienteAppId == tenant.Id)
            .OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).Take(200).Select(h => new PanelEventVm { OrdenId = h.OrdenEntregaId, FechaUtc = h.CreatedAt, Tipo = h.TipoEvento.ToString(), Actor = h.ActorRol, Descripcion = h.Descripcion }).ToListAsync();
        return vm;
    }

    private Task<List<PanelPilotVm>> FlotaAsync(int tenantId) => db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == tenantId).OrderBy(p => p.Nombre)
        .Select(p => new PanelPilotVm { Id = p.Id, Nombre = p.Nombre, Telefono = p.Telefono, Activo = p.Activo, EnLinea = p.EnLinea, Latitud = p.Latitud, Longitud = p.Longitud, GpsUtc = p.UltimaUbicacionAt,
            GpsFrescuraSegundos=GpsFrescuraSegundos,
            ConIncidencia=db.Set<IncidenciaOperativa>().Any(i=>i.ClienteAppId==tenantId&&i.Estado!=EstadoIncidencia.Resuelta
                &&((i.OrdenEntrega!=null&&(i.OrdenEntrega.GestionPorParadas
                    ? i.OrdenEntrega.Paradas.Any(s=>(!i.ParadaEntregaId.HasValue||s.Id==i.ParadaEntregaId)&&(s.PilotoAsignadoId==p.Id||s.PilotoCustodioId==p.Id))
                    : i.OrdenEntrega.PilotoId==p.Id))||(i.Ruta!=null&&i.Ruta.PilotoId==p.Id))),
            Carga = db.OrdenesEntrega.Count(o => o.ClienteAppId==tenantId && (o.GestionPorParadas
                ? o.Paradas.Any(s => (s.Estado!=EstadoParadaEntrega.Entregada&&s.Estado!=EstadoParadaEntrega.Devuelta&&s.Estado!=EstadoParadaEntrega.Cancelada&&s.PilotoAsignadoId==p.Id)
                    ||(s.PilotoCustodioId==p.Id&&s.Estado!=EstadoParadaEntrega.Entregada&&s.Estado!=EstadoParadaEntrega.Devuelta))
                : o.PilotoId==p.Id&&o.Estado!=EstadoOrden.Entregada&&o.Estado!=EstadoOrden.Devuelta&&o.Estado!=EstadoOrden.FinalizadaConDevoluciones&&o.Estado!=EstadoOrden.Cancelada)) }).ToListAsync();

    private IQueryable<OrdenEntrega> Filter(IQueryable<OrdenEntrega> query, string? buscar, string? estado, DateTime? fecha, int? comercioId, int? repartidorId)
    {
        var now=DateTime.UtcNow;
        var future=db.Set<DatosEnvioOperativos>().Where(d=>d.ProgramadaPara>now&&!d.CanceladaAt.HasValue).Select(d=>d.OrdenEntregaId);
        if(estado=="Pendientes")query=query.Where(o=>future.Contains(o.Id)&&o.Estado!=EstadoOrden.Entregada&&o.Estado!=EstadoOrden.Devuelta&&o.Estado!=EstadoOrden.FinalizadaConDevoluciones&&o.Estado!=EstadoOrden.Cancelada);
        if(estado=="Creada")query=query.Where(o=>!future.Contains(o.Id)&&o.PilotoId==null);
        if(estado=="Incidente")query=query.Where(o=>o.Estado==EstadoOrden.Incidente||db.Set<IncidenciaOperativa>().Any(i=>i.OrdenEntregaId==o.Id&&i.Estado!=EstadoIncidencia.Resuelta));
        if(int.TryParse(Request.Query["zonaId"],out var zoneId)&&zoneId>0)query=query.Where(o=>db.Set<DatosEnvioOperativos>().Any(d=>d.OrdenEntregaId==o.Id&&d.ZonaId==zoneId));
        if(Request.Query["demorados"]=="true")query=query.Where(o=>o.Estado!=EstadoOrden.Entregada&&o.Estado!=EstadoOrden.Devuelta&&o.Estado!=EstadoOrden.FinalizadaConDevoluciones&&o.Estado!=EstadoOrden.Cancelada&&db.Set<DatosEnvioOperativos>().Any(d=>d.OrdenEntregaId==o.Id&&d.CompromisoEntrega<now&&!d.CanceladaAt.HasValue));
        if (comercioId.HasValue) query = query.Where(o => o.TiendaId == comercioId.Value);
        if (repartidorId.HasValue) query = query.Where(o => o.GestionPorParadas ? o.Paradas.Any(p=>p.PilotoAsignadoId==repartidorId.Value||p.PilotoCustodioId==repartidorId.Value||p.PilotoEntregaId==repartidorId.Value) : o.PilotoId == repartidorId.Value);
        if(!string.IsNullOrWhiteSpace(estado)&&estado!="Pendientes"&&estado!="Incidente") {if(Enum.TryParse<EstadoOrden>(estado,out var parsed)&&Enum.IsDefined(parsed))query=query.Where(o=>o.Estado==parsed);else query=query.Where(o=>false);}
        if (!string.IsNullOrWhiteSpace(buscar)) { buscar = buscar.Trim(); query = query.Where(o => (o.CodigoA != null && o.CodigoA.Contains(buscar)) || db.Set<DatosEnvioOperativos>().Any(d=>d.ClienteAppId==o.ClienteAppId&&d.OrdenEntregaId==o.Id&&d.Referencia!=null&&d.Referencia.Contains(buscar)) || o.RecogidaNombre.Contains(buscar) || o.DestinoNombre.Contains(buscar) || o.DestinoDireccion.Contains(buscar) || o.Paradas.Any(p=>p.Nombre.Contains(buscar)||p.Direccion.Contains(buscar)) || (o.Piloto!=null&&o.Piloto.Nombre.Contains(buscar)) || (!o.DatosEntregaCongelados && (o.Tienda.Nombre.Contains(buscar) || (o.UsuarioFinal!=null&&o.UsuarioFinal.Nombre.Contains(buscar))))); }
        if (fecha.HasValue && fecha.Value.Year is >= 2000 and <= 9998) { var from = DateTime.SpecifyKind(fecha.Value.Date.AddHours(4), DateTimeKind.Utc); var to = from.AddDays(1); query = query.Where(o => o.CreatedAt >= from && o.CreatedAt < to); }
        return query;
    }

    private async Task EnrichAsync(PanelVm vm)
    {
        var actor=await operations.ManagerAsync(Actor);var snapshot=await operations.SnapshotAsync(actor);vm.Datos=snapshot;
        foreach(var p in vm.Pedidos.Concat(vm.Cola).Concat(vm.Pedido==null?Enumerable.Empty<PanelOrderVm>():new[]{vm.Pedido}))p.Datos=snapshot.DatosEnvios.FirstOrDefault(d=>d.OrdenId==p.Id);
        vm.Incidencias=snapshot.Indicadores?.IncidenciasAbiertas??await db.Set<IncidenciaOperativa>().CountAsync(i=>i.ClienteAppId==actor.TenantId&&i.Estado!=EstadoIncidencia.Resuelta);
        if(vm.Pedido!=null)vm.Devoluciones=await operations.DevolucionesAsync(actor,vm.Pedido.Id);
    }

    private static void MarkUtc(PanelOrderVm p)
    {
        static DateTime Utc(DateTime d)=>DateTime.SpecifyKind(d,DateTimeKind.Utc);
        p.CreadoUtc=Utc(p.CreadoUtc);if(p.AsignadoUtc.HasValue)p.AsignadoUtc=Utc(p.AsignadoUtc.Value);if(p.RecogidoUtc.HasValue)p.RecogidoUtc=Utc(p.RecogidoUtc.Value);if(p.EntregadoUtc.HasValue)p.EntregadoUtc=Utc(p.EntregadoUtc.Value);
        foreach(var stop in p.Paradas)if(stop.EntregadaUtc.HasValue)stop.EntregadaUtc=Utc(stop.EntregadaUtc.Value);
    }

    private async Task AttachOrderDataAsync(List<PanelOrderVm> rows,int tenantId)
    {
        var ids=rows.Select(p=>p.Id).ToArray();var metadata=await db.Set<DatosEnvioOperativos>().AsNoTracking().Where(d=>d.ClienteAppId==tenantId&&ids.Contains(d.OrdenEntregaId)).Select(d=>new DatosEnvioDto(d.OrdenEntregaId,d.Referencia,d.Servicio,d.Paquetes,d.PesoKg,d.Prioridad,d.ZonaId,d.ProgramadaPara,d.CompromisoEntrega,d.CanceladaAt,d.NotasOperativas,d.Version)).ToDictionaryAsync(d=>d.OrdenId);
        foreach(var p in rows){p.Datos=metadata.GetValueOrDefault(p.Id);MarkUtc(p);}
    }

    private static IQueryable<PanelOrderVm> Project(IQueryable<OrdenEntrega> query) => query.Select(o => new PanelOrderVm
    {
        Id = o.Id, Codigo = o.CodigoA ?? o.NumeroOrdenA.ToString(), ComercioId = o.TiendaId,
        Comercio = o.DatosEntregaCongelados ? o.RecogidaNombre : o.Tienda.Nombre, RecogidaDireccion = o.DatosEntregaCongelados ? o.RecogidaDireccion : o.Tienda.Direccion,
        RecogidaTelefono = o.DatosEntregaCongelados ? o.RecogidaTelefono : o.Tienda.Telefono, RecogidaLatitud = o.DatosEntregaCongelados ? o.RecogidaLatitud : o.Tienda.Latitud, RecogidaLongitud = o.DatosEntregaCongelados ? o.RecogidaLongitud : o.Tienda.Longitud,
        Destinatario = o.Paradas.Count>1?o.Paradas.Count+" destinatarios":o.DatosEntregaCongelados ? o.DestinoNombre : o.UsuarioFinal!=null?o.UsuarioFinal.Nombre:"", Direccion = o.Paradas.Count>1?"Varias paradas de entrega":o.DatosEntregaCongelados ? o.DestinoDireccion : o.UsuarioFinal!=null?o.UsuarioFinal.DireccionUbicacion:"",
        Telefono = o.DatosEntregaCongelados ? o.DestinoTelefono : o.UsuarioFinal!=null?o.UsuarioFinal.Telefono:"", DestinoLatitud = o.DatosEntregaCongelados ? o.DestinoLatitud : o.UsuarioFinal!=null?o.UsuarioFinal.Latitud:null, DestinoLongitud = o.DatosEntregaCongelados ? o.DestinoLongitud : o.UsuarioFinal!=null?o.UsuarioFinal.Longitud:null,
        Estado = o.Estado.ToString(), PilotoId = o.PilotoId, Piloto = o.Piloto == null ? null : o.Piloto.Nombre, PilotoTelefono = o.Piloto == null ? null : o.Piloto.Telefono,
        Nota = o.NotaPedido, Auto = o.EsAuto, GestionPorParadas = o.GestionPorParadas, CreadoUtc = o.CreatedAt, AsignadoUtc = o.AssignedAt, RecogidoUtc = o.RecolectadaAt, EntregadoUtc = o.EntregadaAt, Precio = o.Precio, DistanciaKm = o.DistanciaKm,
        Paradas=o.Paradas.OrderBy(p=>p.Posicion).Select(p=>new PanelStopVm{Id=p.Id,Posicion=p.Posicion,Nombre=p.Nombre,Direccion=p.Direccion,Telefono=p.Telefono,Instrucciones=p.Instrucciones,Latitud=p.Latitud,Longitud=p.Longitud,Estado=p.Estado.ToString(),EntregadaUtc=p.EntregadaAt}).ToList()
    });
}
