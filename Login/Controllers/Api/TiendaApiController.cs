using System.Security.Claims;
using Login.Application.Interfaces;
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

        public TiendaApiController(ITiendaAppService svc)
        {
            _svc = svc;
        }

        public sealed record CrearOrdenRequest(int UsuarioFinalId, string? NotaPedido);
        public sealed record CrearUsuarioFinalRequest(string Nombre, string DireccionUbicacion, string Telefono, string? Notas, string? MapaLink);
        public sealed record EditarUsuarioFinalRequest(string Nombre, string DireccionUbicacion, string Telefono, string? Notas, string? MapaLink, bool Activo);
        public sealed record SetTiendaActivaRequest(int TiendaId);

        private string GetUserId()
            => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        // ---------- Tiendas ----------

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
                return _svc.GetTiendaActivaAsync(GetUserId());
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

        [HttpGet("ordenes/{ordenId:int}/qr")]
        public async Task<IActionResult> OrdenQr(int ordenId)
            => await RunAsync(() => _svc.GetOrdenQrAsync(GetUserId(), ordenId));

        [HttpPost("ordenes")]
        public async Task<IActionResult> CrearOrden([FromBody] CrearOrdenRequest request)
            => await RunAsync(() => _svc.CrearOrdenAsync(GetUserId(), request.UsuarioFinalId, request.NotaPedido));

        // ---------- Helper ----------

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
