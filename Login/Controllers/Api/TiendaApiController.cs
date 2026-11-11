using System.Security.Claims;
using Login.Application.Interfaces;
using Login.Application.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Login.Controllers.Api
{
    [ApiController]
    [Route("api/tienda")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Cliente")]
    public class TiendaApiController : ControllerBase
    {
        private readonly ITiendaAppService _svc;
        private readonly ILogger<TiendaApiController> _logger;

        public TiendaApiController(ITiendaAppService svc, ILogger<TiendaApiController> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        public sealed record CrearOrdenRequest(int UsuarioFinalId, string? NotaPedido, string? OperacionId = null);
        public sealed record CrearUsuarioFinalRequest(string Nombre, string DireccionUbicacion, string Telefono, string? Notas, string? MapaLink);
        public sealed record EditarUsuarioFinalRequest(string Nombre, string DireccionUbicacion, string Telefono, string? Notas, string? MapaLink, bool Activo);
        public sealed record SetTiendaActivaRequest(int TiendaId);

        private string GetUserId()
            => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        // ---------- Tiendas ----------

        [HttpGet("capacidades")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Capacidades()
            => Ok(new { versionContrato = 2, creacionIdempotente = true, contextoTienda = true });

        [HttpGet("tiendas")]
        public async Task<IActionResult> Tiendas()
            => await RunAsync(() => _svc.GetTiendasAsync(GetUserId()));

        [HttpGet("tienda-activa")]
        public async Task<IActionResult> TiendaActiva()
            => await RunAsync(() => _svc.GetTiendaActivaAsync(GetUserId()));

        [HttpPost("tienda-activa")]
        public async Task<IActionResult> SetTiendaActiva([FromBody] SetTiendaActivaRequest request)
            => await RunAsync(async () =>
            {
                await _svc.SetTiendaActivaAsync(GetUserId(), request.TiendaId);
                return await _svc.GetTiendaActivaAsync(GetUserId());
            });

        // ---------- Usuarios finales ----------

        [HttpGet("usuarios-finales")]
        public async Task<IActionResult> UsuariosFinales()
            => await RunAsync(() => _svc.GetUsuariosFinalesAsync(GetUserId()));

        [HttpGet("usuarios-finales/{id:int}")]
        public async Task<IActionResult> UsuarioFinal(int id)
            => await RunAsync(() => _svc.GetUsuarioFinalAsync(GetUserId(), id));

        [HttpPost("usuarios-finales")]
        public async Task<IActionResult> CrearUsuarioFinal([FromBody] CrearUsuarioFinalRequest request)
            => await RunAsync(() => _svc.CrearUsuarioFinalAsync(GetUserId(), request.Nombre, request.DireccionUbicacion, request.Telefono, request.Notas, request.MapaLink));

        [HttpPut("usuarios-finales/{id:int}")]
        public async Task<IActionResult> EditarUsuarioFinal(int id, [FromBody] EditarUsuarioFinalRequest request)
            => await RunAsync(() => _svc.EditarUsuarioFinalAsync(GetUserId(), id, request.Nombre, request.DireccionUbicacion, request.Telefono, request.Notas, request.MapaLink, request.Activo));

        [HttpDelete("usuarios-finales/{id:int}")]
        public async Task<IActionResult> EliminarUsuarioFinal(int id)
            => await RunAsync(async () =>
            {
                await _svc.EliminarUsuarioFinalAsync(GetUserId(), id);
                return new { ok = true };
            });

        // ---------- Órdenes ----------

        [HttpGet("ordenes")]
        public async Task<IActionResult> Ordenes()
            => await RunAsync(() => _svc.GetOrdenesAsync(GetUserId()));

        [HttpGet("ordenes/resumen")]
        public async Task<IActionResult> ResumenOrdenes()
            => await RunAsync(() => _svc.GetResumenOrdenesAsync(GetUserId()));

        [HttpGet("ordenes/{ordenId:int}/qr")]
        public async Task<IActionResult> OrdenQr(int ordenId)
            => await RunAsync(() => _svc.GetOrdenQrAsync(GetUserId(), ordenId));

        [HttpPost("ordenes")]
        public async Task<IActionResult> CrearOrden([FromBody] CrearOrdenRequest request)
            => await RunAsync(() => _svc.CrearOrdenAsync(GetUserId(), request.UsuarioFinalId, request.NotaPedido, request.OperacionId));

        // ---------- Helper ----------

        private async Task<IActionResult> RunAsync<T>(Func<Task<T>> action)
        {
            try
            {
                var result = await action();
                return Ok(result);
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
                _logger.LogError(ex, "Error en API de comercio. TraceId {TraceId}, acción {Action}", HttpContext.TraceIdentifier, RouteData.Values["action"]);
                return StatusCode(500, new { error = "Error interno del servidor.", traceId = HttpContext.TraceIdentifier });
            }
        }
    }
}
