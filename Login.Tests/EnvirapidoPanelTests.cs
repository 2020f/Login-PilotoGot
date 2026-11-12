using System.Net;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class EnvirapidoPanelTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private static string Token(string html)
    {
        var match=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [Fact]
    public async Task Tenant_panel_pages_render_with_connected_assets_and_no_nested_forms()
    {
        using var manager=await app.LoginWebAsync("manager-a");
        foreach(var route in new[]{"/Operaciones","/Operaciones/Pedidos","/Operaciones/Rutas","/Operaciones/Incidencias","/Operaciones/Liquidaciones","/Operaciones/Automatizacion","/Operaciones/Programados","/Operaciones/Importaciones","/Operaciones/Reportes","/Operaciones/Configuracion","/Operaciones/CrearEnvio"})
        {
            var response=await manager.GetAsync(route);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var html=await response.Content.ReadAsStringAsync();Assert.Contains("Envirapido",html);Assert.Contains("Tenant A",html);Assert.DoesNotContain("Tenant B",html);Assert.DoesNotContain("Destino privado B",html);
            var depth=0;foreach(Match tag in Regex.Matches(html,"</?form\\b[^>]*>",RegexOptions.IgnoreCase)){depth+=tag.Value.StartsWith("</")?-1:1;Assert.InRange(depth,0,1);}Assert.Equal(0,depth);
            foreach(Match script in Regex.Matches(html,"<script[^>]*src=\"(/[^\"]+\\.js(?:\\?[^\"]*)?)\""))Assert.Equal(HttpStatusCode.OK,(await manager.GetAsync(WebUtility.HtmlDecode(script.Groups[1].Value))).StatusCode);
        }
    }

    [Fact]
    public async Task Order_detail_export_and_assignment_recheck_tenant_and_csrf()
    {
        var orderA=0;var orderB=0;
        await app.InScopeAsync(async sp=>{var svc=sp.GetRequiredService<IOrdenService>();orderA=(await svc.CrearOrdenAsync(app.TenantA,app.ShopA,app.CustomerA)).OrdenId;orderB=(await svc.CrearOrdenAsync(app.TenantB,app.ShopB,app.CustomerB)).OrdenId;});
        using var manager=await app.LoginWebAsync("manager-a");
        Assert.Equal(HttpStatusCode.OK,(await manager.GetAsync($"/Operaciones/Pedido/{orderA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await manager.GetAsync($"/Operaciones/Pedido/{orderB}")).StatusCode);
        var csv=await manager.GetStringAsync($"/Operaciones/Exportar?ids={orderA},{orderB}");Assert.Contains("Destino A",csv);Assert.DoesNotContain("Destino B",csv);Assert.DoesNotContain("CodigoC",csv);Assert.DoesNotContain("CodigoB",csv);
        var noToken=await manager.PostAsync("/Operaciones/Asignar",new FormUrlEncodedContent(new Dictionary<string,string>{{"ordenId",orderA.ToString()},{"pilotoId",app.PilotA.ToString()}}));Assert.Equal(HttpStatusCode.BadRequest,noToken.StatusCode);
        var html=await manager.GetStringAsync("/Operaciones");manager.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        var foreignPilot=await manager.PostAsync("/Operaciones/Asignar",new FormUrlEncodedContent(new Dictionary<string,string>{{"ordenId",orderA.ToString()},{"pilotoId",app.PilotB.ToString()},{"__RequestVerificationToken",Token(html)}}));Assert.Equal(HttpStatusCode.BadRequest,foreignPilot.StatusCode);
        await app.InScopeAsync(async sp=>Assert.Null((await sp.GetRequiredService<ApplicationDbContext>().OrdenesEntrega.FindAsync(orderA))!.PilotoId));
    }

    [Fact]
    public async Task Future_scheduled_orders_are_pending_and_not_ready_for_assignment()
    {
        var id=0;
        await app.InScopeAsync(async sp=>{id=(await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA,app.ShopA,app.CustomerA)).OrdenId;var db=sp.GetRequiredService<ApplicationDbContext>();db.Set<DatosEnvioOperativos>().Add(new(){ClienteAppId=app.TenantA,OrdenEntregaId=id,ProgramadaPara=DateTime.UtcNow.AddDays(1)});await db.SaveChangesAsync();});
        using var manager=await app.LoginWebAsync("manager-a");
        Assert.Contains($"data-order-id=\"{id}\"",await manager.GetStringAsync("/Operaciones/Fragmento?seccion=Pedidos&estado=Pendientes"));
        Assert.DoesNotContain($"data-order-id=\"{id}\"",await manager.GetStringAsync("/Operaciones/Fragmento?seccion=Pedidos&estado=Creada"));
    }

    [Fact]
    public async Task Merchant_web_creates_one_multistop_order_and_recovers_same_receipt_on_retry()
    {
        var second=0;await app.InScopeAsync(async sp=>{var db=sp.GetRequiredService<ApplicationDbContext>();var p=new UsuarioFinal{TiendaId=app.ShopA,Nombre="Segundo destino",DireccionUbicacion="Segunda entrega",Telefono="8090001"};db.UsuariosFinales.Add(p);await db.SaveChangesAsync();second=p.Id;});
        using var shop=await app.LoginWebAsync("shop-a");var page=await shop.GetStringAsync("/Tienda/CrearOrden");var operation=Guid.NewGuid().ToString("N");
        var input=new Dictionary<string,string>{{"TiendaId",app.ShopA.ToString()},{"EsEscala","true"},{"UsuarioFinalId","0"},{"Destinatarios[0].UsuarioFinalId",app.CustomerA.ToString()},{"Destinatarios[1].UsuarioFinalId",second.ToString()},{"OperacionId",operation},{"__RequestVerificationToken",Token(page)}};
        var first=await shop.PostAsync("/Tienda/CrearOrdenPost",new FormUrlEncodedContent(input));Assert.Equal(HttpStatusCode.Redirect,first.StatusCode);
        var retry=await shop.PostAsync("/Tienda/CrearOrdenPost",new FormUrlEncodedContent(input));Assert.Equal(first.Headers.Location,retry.Headers.Location);
        var receipt=await shop.GetStringAsync(first.Headers.Location);Assert.Contains("Parada 1",receipt);Assert.Contains("Parada 2",receipt);Assert.Contains("C individual",receipt);
        await app.InScopeAsync(async sp=>{var db=sp.GetRequiredService<ApplicationDbContext>();Assert.Equal(1,await db.OrdenesEntrega.CountAsync(o=>o.ClienteAppId==app.TenantA));Assert.Equal(2,await db.Set<ParadaEntrega>().CountAsync(p=>p.ClienteAppId==app.TenantA));});
    }

    [Fact]
    public async Task Csv_preview_does_not_write_and_confirmation_replays_without_importing_foreign_tenant()
    {
        using var manager=await app.LoginWebAsync("manager-a");var page=await manager.GetStringAsync("/Operaciones/Importaciones");
        using var upload=new MultipartFormDataContent();upload.Add(new StringContent(Token(page)),"__RequestVerificationToken");upload.Add(new StringContent($"ComercioId,DestinatarioIds,Nota\r\n{app.ShopA},{app.CustomerA},Pedido importado\r\n{app.ShopB},{app.CustomerB},Ajeno\r\n"),"archivo","ordenes.csv");
        var preview=await manager.PostAsync("/Operaciones/PrevisualizarImportacion",upload);Assert.Equal(HttpStatusCode.OK,preview.StatusCode);var html=await preview.Content.ReadAsStringAsync();Assert.Contains("no pertenece a tu empresa",html);Assert.DoesNotContain("Destino privado B",html);
        await app.InScopeAsync(async sp=>Assert.Equal(0,await sp.GetRequiredService<ApplicationDbContext>().OrdenesEntrega.CountAsync()));
        var key=Guid.NewGuid().ToString("N");var input=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"Filas[0].Linea","2"},{"Filas[0].ComercioId",app.ShopA.ToString()},{"Filas[0].DestinatarioIds",app.CustomerA.ToString()},{"Filas[0].Nota","Pedido importado"},{"Filas[0].OperacionId",key},{"Filas[1].Linea","3"},{"Filas[1].ComercioId",app.ShopB.ToString()},{"Filas[1].DestinatarioIds",app.CustomerB.ToString()},{"Filas[1].OperacionId",Guid.NewGuid().ToString("N")}};
        for(var attempt=0;attempt<2;attempt++){var confirm=await manager.PostAsync("/Operaciones/ConfirmarImportacion",new FormUrlEncodedContent(input));Assert.Equal(HttpStatusCode.OK,confirm.StatusCode);var result=await confirm.Content.ReadAsStringAsync();Assert.Contains("Creada EV-",result);Assert.Contains("no pertenece a tu empresa",result);}
        await app.InScopeAsync(async sp=>{var db=sp.GetRequiredService<ApplicationDbContext>();Assert.Equal(1,await db.OrdenesEntrega.CountAsync());Assert.Equal(1,await db.OperacionesOrden.CountAsync(o=>o.ClienteAppId==app.TenantA&&o.Tipo=="ImportarOrden"&&o.Clave==key));Assert.Equal(0,await db.OrdenesEntrega.CountAsync(o=>o.ClienteAppId==app.TenantB));});
    }

    [Fact]
    public async Task Manager_can_open_every_multistop_order_tab_and_select_a_stop_before_pickup()
    {
        var id=0;var stops=new List<int>();var pickupCode="";
        await app.InScopeAsync(async sp=>{
            var db=sp.GetRequiredService<ApplicationDbContext>();var second=new UsuarioFinal{TiendaId=app.ShopA,Nombre="Destinatario dos",DireccionUbicacion="Dirección segunda parada",Telefono="8090002",Latitud=18.48,Longitud=-69.93};db.UsuariosFinales.Add(second);
            var pilot=await db.Pilotos.FindAsync(app.PilotA2);pilot!.Latitud=18.49;pilot.Longitud=-69.94;pilot.UltimaUbicacionAt=app.Clock.GetUtcNow().UtcDateTime;await db.SaveChangesAsync();
            var created=await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA,app.ShopA,app.CustomerA,null,"manager-a","Gestor",Guid.NewGuid().ToString("N"),new[]{new OrdenDestinatarioRequest(app.CustomerA),new OrdenDestinatarioRequest(second.Id)});id=created.OrdenId;pickupCode=created.CodigoB;stops=await db.Set<ParadaEntrega>().Where(p=>p.OrdenEntregaId==id).OrderBy(p=>p.Posicion).Select(p=>p.Id).ToListAsync();
            var operations=sp.GetRequiredService<EnvirapidoOperationsService>();var actor=await operations.ManagerAsync("manager-a");await operations.CrearRutaAsync(actor,new CrearRutaRequest("Ruta de regresión",DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-4)),new List<int>{id},app.PilotA2,null,false,Guid.NewGuid().ToString("N")));
            var route=await db.Set<RutaOperativa>().SingleAsync(r=>r.ClienteAppId==app.TenantA);await operations.EstadoRutaAsync(actor,route.Id,new EstadoRutaRequest("Iniciar",route.Version,Guid.NewGuid().ToString("N")));
            var order=await db.OrdenesEntrega.SingleAsync(o=>o.Id==id);Assert.Null(order.UsuarioFinalId);Assert.Null(order.RecolectadaAt);Assert.Equal(app.PilotA2,order.PilotoId);
        });
        using var manager=await app.LoginWebAsync("manager-a");
        foreach(var tab in new[]{"Resumen","Seguimiento","Historial","Evidencias","Costos"})
        {
            var response=await manager.GetAsync($"/Operaciones/Pedido/{id}?tab={tab}");var body=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==HttpStatusCode.OK,$"Detalle multiparada/{tab}: {(int)response.StatusCode} {body[..Math.Min(body.Length,2000)]}");Assert.Contains("Destinatario dos",body);Assert.DoesNotContain("Destino privado B",body);
        }
        var selected=await manager.GetAsync($"/Operaciones/Pedido/{id}?paradaId={stops[1]}");Assert.Equal(HttpStatusCode.OK,selected.StatusCode);Assert.Contains("PARADA 2",await selected.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await manager.GetAsync($"/Operaciones/Pedido/{id}?paradaId=999999")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await manager.GetAsync($"/Operaciones/Seguimientos?ids={id}")).StatusCode);
        await app.InScopeAsync(async sp=>await sp.GetRequiredService<IOrdenService>().ConfirmarRecolectaAsync(pickupCode,"pilot-a2","Piloto",id));
        foreach(var stop in stops){Assert.Equal(HttpStatusCode.OK,(await manager.GetAsync($"/Operaciones/Pedido/{id}?tab=Seguimiento&paradaId={stop}")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await manager.GetAsync($"/Operaciones/Seguimiento/{id}?paradaId={stop}")).StatusCode);}
    }
}
