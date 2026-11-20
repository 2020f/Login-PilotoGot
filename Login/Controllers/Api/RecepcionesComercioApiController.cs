using System.Security.Claims;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Login.Controllers.Api;

[ApiController]
[Route("api/tienda/recepciones")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Cliente")]
public sealed class RecepcionesComercioApiController(EnvirapidoOperationsService service, IUserContextService context,
    ILogger<RecepcionesComercioApiController> logger) : ControllerBase
{
    private async Task<OperationsActor> ActorAsync(int tiendaId)
    {
        var user = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (tiendaId <= 0 || await context.GetTiendaActivaIdAsync(user) != tiendaId)
            throw new OperationsException(409, "El comercio activo cambió. Actualiza las recepciones.");
        return await service.ReceptorComercioAsync(user, tiendaId);
    }
    [HttpGet]
    public Task<IActionResult> Listar(int tiendaId) => OperationsResponses.RunAsync(this, logger,
        async () => await service.ReasignacionesAsync(await ActorAsync(tiendaId)));
    [HttpPost("{id:int}")]
    public Task<IActionResult> Confirmar(int id, int tiendaId, ConfirmarRecepcionParadaRequest r)
        => OperationsResponses.RunAsync(this, logger, async () => await service.ConfirmarRecepcionParadaAsync(await ActorAsync(tiendaId), id, r));
}
