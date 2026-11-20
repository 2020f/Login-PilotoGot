using Login.Application.Operations;
using Microsoft.AspNetCore.Mvc;

namespace Login.Controllers.Api;

public sealed partial class OperationsApiController
{
    [HttpGet("reasignaciones")]
    public Task<IActionResult> Reasignaciones(int? ordenId = null) => Run(async a => await service.ReasignacionesAsync(a, ordenId));
    [HttpPost("retiros")]
    public Task<IActionResult> SolicitarRetiro(SolicitarRetiroParadasRequest r) => Run(a => service.SolicitarRetiroParadasAsync(a, r));
    [HttpPost("retiros/{id:int}/decidir")]
    public Task<IActionResult> DecidirRetiro(int id, DecidirRetiroParadaRequest r) => Run(a => service.DecidirRetiroParadaAsync(a, id, r));
    [HttpPost("retiros/{id:int}/ajuste")]
    public Task<IActionResult> AjusteRetiro(int id, RegistrarAjusteRetiroRequest r) => Run(a => service.RegistrarAjusteRetiroAsync(a, id, r));
    [HttpPost("reasignar")]
    public Task<IActionResult> Reasignar(AsignarParadasRequest r) => Run(a => service.AsignarParadasAsync(a, r));
    [HttpPost("retornos/{id:int}")]
    public Task<IActionResult> Retorno(int id, ProgramarRetornoParadaRequest r) => Run(a => service.ProgramarRetornoParadaAsync(a, id, r));
    [HttpPost("recepciones/{id:int}")]
    public Task<IActionResult> Recepcion(int id, ConfirmarRecepcionParadaRequest r) => Run(a => service.ConfirmarRecepcionParadaAsync(a, id, r));
    [HttpPost("cancelar-paradas")]
    public Task<IActionResult> CancelarParadas(CancelarParadasRequest r) => Run(a => service.CancelarParadasAsync(a, r));
    [HttpPost("devoluciones-a-reasignacion")]
    public Task<IActionResult> TrasladarDevoluciones(TrasladarDevolucionesRequest r) => Run(a => service.TrasladarDevolucionesAReasignacionAsync(a, r));
}

public sealed partial class PilotOperationsApiController
{
    [HttpGet("reasignaciones")]
    public Task<IActionResult> Reasignaciones() => Run(async a => await service.ReasignacionesAsync(a));
    [HttpPost("retiros")]
    public Task<IActionResult> SolicitarRetiro(SolicitarRetiroParadasRequest r) => Run(a => service.SolicitarRetiroParadasAsync(a, r));
    [HttpPost("recepciones/{id:int}")]
    public Task<IActionResult> Recepcion(int id, ConfirmarRecepcionParadaRequest r) => Run(a => service.ConfirmarRecepcionParadaAsync(a, id, r));
}
