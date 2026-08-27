using System.Security.Claims;
using Login.Application.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Login.Controllers.Api
{
    [ApiController]
    [Route("api/piloto")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Piloto")]
    public class PilotoApiController : ControllerBase
    {
        private readonly IPilotoAppService _svc;

        public PilotoApiController(IPilotoAppService svc)
        {
            _svc = svc;
        }

        public sealed record ConfirmarRecolectaRequest(string CodigoB);
        public sealed record ConfirmarEntregaRequest(string CodigoB, string CodigoC);

        private string GetUserId()
            => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        [HttpGet("mi-orden")]
        public async Task<IActionResult> MiOrden()
            => await RunAsync(() => _svc.GetOrdenActualAsync(GetUserId()));

        [HttpPost("confirmar-recolecta")]
        public async Task<IActionResult> ConfirmarRecolecta([FromBody] ConfirmarRecolectaRequest request)
            => await RunAsync(async () =>
            {
                await _svc.ConfirmarRecolectaAsync(GetUserId(), request.CodigoB);
                return new { ok = true };
            });

        [HttpPost("confirmar-entrega")]
        public async Task<IActionResult> ConfirmarEntrega([FromBody] ConfirmarEntregaRequest request)
            => await RunAsync(async () =>
            {
                await _svc.ConfirmarEntregaAsync(GetUserId(), request.CodigoB, request.CodigoC);
                return new { ok = true };
            });

        private async Task<IActionResult> RunAsync<T>(Func<Task<T>> action)
        {
            try
            {
                var result = await action();
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Error interno del servidor." });
            }
        }
    }
}
