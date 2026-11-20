using Login.Application.Operations;
using Microsoft.AspNetCore.Mvc;

namespace Login.Controllers.Api;

public sealed partial class OperationsApiController
{
    [HttpGet("devoluciones")]
    public Task<IActionResult> Devoluciones(int? ordenId = null) => Run(async a => await service.DevolucionesAsync(a, ordenId));
    [HttpPost("devoluciones")]
    public Task<IActionResult> SolicitarDevolucion(SolicitarDevolucionRequest r) => Run(a => service.SolicitarDevolucionAsync(a, r));
    [HttpPost("devoluciones/{id:int}/decidir")]
    public Task<IActionResult> DecidirDevolucion(int id, DecidirDevolucionRequest r) => Run(a => service.DecidirDevolucionAsync(a, id, r));
    [HttpPost("devoluciones/{id:int}/recibir")]
    public Task<IActionResult> RecibirDevolucion(int id, RecibirDevolucionRequest r) => Run(a => service.RecibirDevolucionAsync(a, id, r));
    [HttpPost("ordenes/{id:int}/cancelar")]
    public Task<IActionResult> CancelarOrden(int id, CancelarOrdenRequest r) => Run(a => service.CancelarOrdenAsync(a, id, r));
}

public sealed partial class PilotOperationsApiController
{
    [HttpGet("devoluciones")]
    public Task<IActionResult> Devoluciones(int? ordenId = null) => Run(async a => await service.DevolucionesAsync(a, ordenId));
    [HttpPost("devoluciones")]
    public Task<IActionResult> SolicitarDevolucion(SolicitarDevolucionRequest r) => Run(a => service.SolicitarDevolucionAsync(a, r));
    [HttpPost("devoluciones/{id:int}/iniciar-retorno")]
    public Task<IActionResult> IniciarRetorno(int id, OperacionVersionRequest r) => Run(a => service.IniciarRetornoAsync(a, id, r));
}
