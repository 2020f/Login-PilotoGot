using System.Security.Claims;
using Login.Application.Operations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Login.Controllers.Api;

public static class OperationsResponses
{
    public static async Task<IActionResult> RunAsync(ControllerBase controller, ILogger logger, Func<Task<object>> action)
    {
        try { return controller.Ok(await action()); }
        catch (RejectedOperationException e) { return controller.StatusCode(e.StatusCode, new { error = e.Message, solicitudRechazada = true }); }
        catch (OperationsException e) { return controller.StatusCode(e.StatusCode, new { error = e.Message }); }
        catch (InvalidOperationException e) when (e.Message == "Empresa no disponible.")
        { return controller.StatusCode(403, new { error = e.Message }); }
        catch (Exception e)
        {
            logger.LogError(e, "Operación no completada. TraceId {TraceId}", controller.HttpContext.TraceIdentifier);
            return controller.StatusCode(500, new { error = "No se pudo confirmar la operación. Conserva el intento y vuelve a consultar.", traceId = controller.HttpContext.TraceIdentifier });
        }
    }
}

[ApiController]
[Route("api/operaciones")]
[Authorize(Roles = "Gestor")]
[AutoValidateAntiforgeryToken]
public sealed partial class OperationsApiController(EnvirapidoOperationsService service, ILogger<OperationsApiController> logger) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private Task<IActionResult> Run(Func<OperationsActor, Task<object>> action)
        => OperationsResponses.RunAsync(this, logger, async () => await action(await service.ManagerAsync(UserId)));
    [HttpGet("snapshot")]
    public Task<IActionResult> Snapshot(DateOnly? desde = null, DateOnly? hasta = null)
        => Run(async a => await service.SnapshotAsync(a, desde, hasta));
    [HttpPost("zonas")]
    public Task<IActionResult> Zona(GuardarZonaRequest r) => Run(a => service.GuardarZonaAsync(a, r));
    [HttpPost("rutas")]
    public Task<IActionResult> Ruta(CrearRutaRequest r) => Run(a => service.CrearRutaAsync(a, r));
    [HttpPost("rutas/{id:int}/asignar")]
    public Task<IActionResult> Asignar(int id, AsignarRutaRequest r) => Run(a => service.AsignarRutaAsync(a, id, r));
    [HttpPost("rutas/{id:int}/reordenar")]
    public Task<IActionResult> Reordenar(int id, ReordenarRutaRequest r) => Run(a => service.ReordenarRutaAsync(a, id, r));
    [HttpPost("rutas/{id:int}/estado")]
    public Task<IActionResult> EstadoRuta(int id, EstadoRutaRequest r) => Run(a => service.EstadoRutaAsync(a, id, r));
    [HttpPost("incidencias")]
    public Task<IActionResult> Reportar(ReportarIncidenciaRequest r) => Run(a => service.ReportarIncidenciaAsync(a, r));
    [HttpPost("incidencias/{id:int}")]
    public Task<IActionResult> Incidencia(int id, CambiarIncidenciaRequest r) => Run(a => service.CambiarIncidenciaAsync(a, id, r));
    [HttpPost("incidencias/detectar-demoras")]
    public Task<IActionResult> Detectar(LlegadaPuntoRequest r) => Run(a => service.DetectarDemorasAsync(a, r.OperacionId));
    [HttpPost("reglas-incidencia")]
    public Task<IActionResult> ReglaIncidencia(GuardarReglaIncidenciaRequest r) => Run(a => service.GuardarReglaIncidenciaAsync(a, r));
    [HttpPost("reglas-remuneracion")]
    public Task<IActionResult> ReglaPago(GuardarReglaRemuneracionRequest r) => Run(a => service.GuardarReglaRemuneracionAsync(a, r));
    [HttpPost("liquidaciones")]
    public Task<IActionResult> Generar(GenerarLiquidacionRequest r) => Run(a => service.GenerarLiquidacionAsync(a, r));
    [HttpPost("liquidaciones/{id:int}")]
    public Task<IActionResult> Liquidacion(int id, CambiarLiquidacionRequest r) => Run(a => service.CambiarLiquidacionAsync(a, id, r));
    [HttpPost("liquidaciones/{id:int}/ajustes")]
    public Task<IActionResult> Ajustar(int id, AjustarLiquidacionRequest r) => Run(a => service.AjustarLiquidacionAsync(a, id, r));
    [HttpPost("cobros")]
    public Task<IActionResult> Cobro(RegistrarCobroRequest r) => Run(a => service.RegistrarCobroAsync(a, r));
    [HttpPost("cobros/{id:int}/conciliar")]
    public Task<IActionResult> Conciliar(int id, ConciliarCobroRequest r) => Run(a => service.ConciliarCobroAsync(a, id, r));
    [HttpPost("cobros/{id:int}/recibir")]
    public Task<IActionResult> Recibir(int id, RecibirEfectivoRequest r) => Run(a => service.RecibirEfectivoAsync(a, id, r));
    [HttpPost("ordenes/{id:int}/datos")]
    public Task<IActionResult> Datos(int id, GuardarDatosEnvioRequest r) => Run(a => service.GuardarDatosEnvioAsync(a, id, r));
}

[ApiController]
[Route("api/piloto/operaciones")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Piloto")]
public sealed partial class PilotOperationsApiController(EnvirapidoOperationsService service, ILogger<PilotOperationsApiController> logger) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private Task<IActionResult> Run(Func<OperationsActor, Task<object>> action)
        => OperationsResponses.RunAsync(this, logger, async () => await action(await service.PilotAsync(UserId)));
    [HttpGet]
    public Task<IActionResult> Snapshot() => Run(async a => await service.SnapshotAsync(a));
    [HttpGet("actividad")]
    public Task<IActionResult> Actividad() => Run(async a => await service.ActividadAsync(a));
    [HttpPost("incidencias")]
    public Task<IActionResult> Reportar(ReportarIncidenciaRequest r) => Run(a => service.ReportarIncidenciaAsync(a, r));
    [HttpPost("incidencias/{id:int}/notas")]
    public Task<IActionResult> Nota(int id, CambiarIncidenciaRequest r) => Run(a => service.CambiarIncidenciaAsync(a, id, r));
    [HttpPost("rutas/{id:int}/reordenar")]
    public Task<IActionResult> Reordenar(int id, ReordenarRutaRequest r) => Run(a => service.ReordenarRutaAsync(a, id, r));
    [HttpPost("rutas/{ruta:int}/puntos/{punto:int}/llegada")]
    public Task<IActionResult> Llegada(int ruta, int punto, LlegadaPuntoRequest r) => Run(a => service.LlegadaAsync(a, ruta, punto, r));
    [HttpPost("ubicaciones")]
    public Task<IActionResult> Ubicaciones(EnviarGpsRequest r) => Run(a => service.EnviarGpsAsync(a, r));
    [HttpPost("cobros")]
    public Task<IActionResult> Cobro(RegistrarCobroRequest r) => Run(a => service.RegistrarCobroAsync(a, r));
}
