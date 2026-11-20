using System.Security.Claims;
using Login.Application.Interfaces;
using Login.Application.Exceptions;
using Login.Application.Operations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Login.Controllers.Api
{
    [ApiController]
    [Route("api/piloto")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Piloto")]
    public class PilotoApiController : ControllerBase
    {
        private readonly IPilotoAppService _svc;
        private readonly ILogger<PilotoApiController> _logger;

        public PilotoApiController(IPilotoAppService svc, ILogger<PilotoApiController> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        public sealed record ConfirmarRecolectaRequest(string CodigoB, int? OrdenId = null, string? OperacionId = null);
        public sealed record ConfirmarEntregaRequest(string? CodigoB, string CodigoC, int? OrdenId = null);
        public sealed record ConfirmarParadaRequest(string CodigoC, string OperacionId, string? RevisionAsignacion = null);
        public sealed record ConfirmarRecogidaParadasRequest(string CodigoB, string OperacionId,
            IReadOnlyList<RevisionParadaRequest> Paradas);
        public sealed record UbicacionRequest(double Latitud, double Longitud);
        public sealed record EstadoRequest(bool EnLinea);

        private string GetUserId()
            => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        private bool SoportaParadas => int.TryParse(Request.Headers["X-Contrato-Ordenes"].ToString(), out var v) && v >= 3;
        private bool SoportaAsignaciones => int.TryParse(Request.Headers["X-Contrato-Ordenes"].ToString(), out var v) && v >= 4;

        [HttpGet("capacidades")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Capacidades() => Ok(new { versionContrato = 4, multiparada = true,
            cierreSoloC = true, confirmacionParadaIdempotente = true, gestionPorParadas = true,
            recogidaPorLoteIdempotente = true, revisionAsignacion = true, retiroVinculadoAIncidencia = true });

        [HttpPost("ordenes/{ordenId:int}/paradas/{paradaId:int}/confirmar-entrega")]
        [EnableRateLimiting("CodigosEntrega")]
        public async Task<IActionResult> ConfirmarParada(int ordenId, int paradaId, [FromBody] ConfirmarParadaRequest request)
            => !SoportaAsignaciones && request.RevisionAsignacion is not null
                ? StatusCode(426, new { error = "Actualiza la app para confirmar una asignación por parada." })
                : await RunAsync(() => _svc.ConfirmarEntregaParadaAsync(GetUserId(), ordenId, paradaId,
                    request.CodigoC, request.OperacionId, request.RevisionAsignacion));

        [HttpPost("ordenes/{ordenId:int}/recoger-paradas")]
        [EnableRateLimiting("CodigosEntrega")]
        public async Task<IActionResult> RecogerParadas(int ordenId, [FromBody] ConfirmarRecogidaParadasRequest request)
            => !SoportaAsignaciones ? StatusCode(426, new { error = "Actualiza la app para gestionar recogidas por parada." })
                : await RunAsync(() => _svc.ConfirmarRecogidaParadasAsync(GetUserId(), ordenId,
                    request.CodigoB, request.Paradas, request.OperacionId));

        [HttpGet("mi-orden")]
        public async Task<IActionResult> MiOrden()
            => await RunAsync(async () => (await _svc.GetOrdenesAsync(GetUserId()))
                .FirstOrDefault(o => (!o.GestionPorParadas || SoportaAsignaciones) && (SoportaParadas || o.Paradas.Count <= 1)));

        [HttpGet("historial")]
        public async Task<IActionResult> Historial(int pagina = 1, int tamano = 30)
            => await RunAsync(() => _svc.GetHistorialAsync(GetUserId(), pagina, tamano, SoportaAsignaciones));

        [HttpGet("historial/{ordenId:int}")]
        public async Task<IActionResult> Historica(int ordenId)
            => await RunAsync(async () => {
                var orden = await _svc.GetOrdenHistoricaAsync(GetUserId(), ordenId);
                if (orden?.GestionPorParadas == true && !SoportaAsignaciones) throw new OperationsException(426, "Actualiza la app para consultar esta asignación por parada.");
                return orden;
            });

        [HttpGet("mis-ordenes")]
        public async Task<IActionResult> MisOrdenes()
            => await RunAsync(async () => (await _svc.GetOrdenesAsync(GetUserId()))
                .Where(o => (!o.GestionPorParadas || SoportaAsignaciones) && (SoportaParadas || o.Paradas.Count <= 1)).ToList());

        [HttpGet("ordenes/{ordenId:int}")]
        public async Task<IActionResult> OrdenDetalle(int ordenId)
            => await RunAsync(async () => {
                var orden = await _svc.GetOrdenDetalleAsync(GetUserId(), ordenId);
                if (orden?.GestionPorParadas == true && !SoportaAsignaciones) throw new OperationsException(426, "Actualiza la app para consultar esta asignación por parada.");
                return orden is not null && (SoportaParadas || orden.Paradas.Count <= 1) ? orden : null;
            });

        [HttpPost("confirmar-recolecta")]
        [EnableRateLimiting("CodigosEntrega")]
        public async Task<IActionResult> ConfirmarRecolecta([FromBody] ConfirmarRecolectaRequest request)
            => await RunAsync(async () =>
            {
                if (!SoportaParadas && request.OrdenId.HasValue
                    && (await _svc.GetOrdenDetalleAsync(GetUserId(), request.OrdenId.Value))?.Paradas.Count > 1)
                    throw new InvalidOperationException("Actualiza la app para recoger una orden con varias paradas.");
                await _svc.ConfirmarRecolectaAsync(GetUserId(), request.CodigoB, request.OrdenId, request.OperacionId);
                return new { ok = true };
            });

        [HttpPost("confirmar-entrega")]
        [EnableRateLimiting("CodigosEntrega")]
        public async Task<IActionResult> ConfirmarEntrega([FromBody] ConfirmarEntregaRequest request)
            => await RunAsync(async () =>
            {
                await _svc.ConfirmarEntregaAsync(GetUserId(), request.CodigoB ?? "", request.CodigoC, request.OrdenId);
                return new { ok = true };
            });

        // ---------- Auto-asignación ----------

        [HttpGet("ordenes-auto")]
        public async Task<IActionResult> OrdenesAuto()
            => await RunAsync(async () => (await _svc.GetOrdenesAutoAsync(GetUserId()))
                .Where(o => SoportaParadas || o.TotalParadas <= 1).ToList());

        [HttpPost("ordenes-auto/{ordenId:int}/aceptar")]
        public async Task<IActionResult> AceptarOrdenAuto(int ordenId)
            => await RunAsync(async () =>
            {
                if (!SoportaParadas && (await _svc.GetOrdenesAutoAsync(GetUserId()))
                    .Any(o => o.OrdenId == ordenId && o.TotalParadas > 1))
                    throw new InvalidOperationException("Actualiza la app para aceptar una orden con varias paradas.");
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
            catch (RejectedOperationException ex)
            {
                return StatusCode(ex.StatusCode, new { error = ex.Message, solicitudRechazada = true });
            }
            catch (OperationsException ex)
            {
                return StatusCode(ex.StatusCode, new { error = ex.Message });
            }
            catch (OperacionOrdenConflictoException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (OrdenSolicitudRechazadaException ex)
            {
                return BadRequest(new { error = ex.Message, solicitudRechazada = true });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en API de piloto. TraceId {TraceId}, acción {Action}", HttpContext.TraceIdentifier, RouteData.Values["action"]);
                return StatusCode(500, new { error = "Error interno del servidor.", traceId = HttpContext.TraceIdentifier });
            }
        }
    }
}
