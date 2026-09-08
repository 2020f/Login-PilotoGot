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
        public sealed record UbicacionRequest(double Latitud, double Longitud);
        public sealed record EstadoRequest(bool EnLinea);

        private string GetUserId()
            => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        [HttpGet("mi-orden")]
        public async Task<IActionResult> MiOrden()
            => await RunAsync(() => _svc.GetOrdenActualAsync(GetUserId()));

        [HttpGet("mis-ordenes")]
        public async Task<IActionResult> MisOrdenes()
            => await RunAsync(() => _svc.GetOrdenesAsync(GetUserId()));

        [HttpGet("ordenes/{ordenId:int}")]
        public async Task<IActionResult> OrdenDetalle(int ordenId)
            => await RunAsync(() => _svc.GetOrdenDetalleAsync(GetUserId(), ordenId));

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

        // ---------- Auto-asignación ----------

        [HttpGet("ordenes-auto")]
        public async Task<IActionResult> OrdenesAuto()
            => await RunAsync(() => _svc.GetOrdenesAutoAsync(GetUserId()));

        [HttpPost("ordenes-auto/{ordenId:int}/aceptar")]
        public async Task<IActionResult> AceptarOrdenAuto(int ordenId)
            => await RunAsync(async () =>
            {
                await _svc.AceptarOrdenAutoAsync(GetUserId(), ordenId);
                return new { ok = true };
            });

        [HttpPost("ordenes-auto/{ordenId:int}/rechazar")]
        public async Task<IActionResult> RechazarOrdenAuto(int ordenId)
            => await RunAsync(async () =>
            {
                await _svc.RechazarOrdenAutoAsync(GetUserId(), ordenId);
                return new { ok = true };
            });

        // ---------- GPS ----------

        [HttpPost("ubicacion")]
        public async Task<IActionResult> ActualizarUbicacion([FromBody] UbicacionRequest request)
            => await RunAsync(async () =>
            {
                await _svc.ActualizarUbicacionAsync(GetUserId(), request.Latitud, request.Longitud);
                return new { ok = true };
            });

        // ---------- Estado (en línea / desconectado) ----------

        [HttpGet("estado")]
        public async Task<IActionResult> Estado()
            => await RunAsync(() => _svc.GetEstadoAsync(GetUserId()));

        [HttpPost("estado")]
        public async Task<IActionResult> SetEstado([FromBody] EstadoRequest request)
            => await RunAsync(async () =>
            {
                await _svc.SetEnLineaAsync(GetUserId(), request.EnLinea);
                return await _svc.GetEstadoAsync(GetUserId());
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
