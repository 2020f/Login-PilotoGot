using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Login.ViewModels.Piloto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers
{
    [Authorize(Roles = "Piloto")]
    public class PilotoController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IOrdenService _ordenService;
        private readonly IPilotoAppService _app;

        public PilotoController(ApplicationDbContext db, IOrdenService ordenService, IPilotoAppService app)
        {
            _db = db;
            _ordenService = ordenService;
            _app = app;
        }

        private async Task<Login.Domain.Entities.Piloto?> GetPilotoLogueadoAsync(string identityUserId)
        {
            return await _db.Pilotos.AsNoTracking()
                .SingleOrDefaultAsync(p => p.IdentityUserId == identityUserId && p.Activo);
        }

        // GET: /Piloto
        [HttpGet]
        public async Task<IActionResult> Index(string? ok = null, string? error = null)
        {
            var vm = new MisOrdenesIndexVm { Ok = ok, Error = error };

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Redirect("/Identity/Account/Login");

            var piloto = await GetPilotoLogueadoAsync(userId);
            if (piloto is null) return Forbid();

            var ordenes = (await _app.GetOrdenesAsync(userId)).Select(Map).ToList();

            vm.Ordenes = ordenes;

            return View(vm);
        }

        // GET: /Piloto/Detalle/{id}
        [HttpGet]
        public async Task<IActionResult> Detalle(int id, string? ok = null, string? error = null)
        {
            var vm = new MisOrdenesIndexVm { Ok = ok, Error = error };

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Redirect("/Identity/Account/Login");

            var piloto = await GetPilotoLogueadoAsync(userId);
            if (piloto is null) return Forbid();

            var detalle = await _app.GetOrdenDetalleAsync(userId, id);
            var orden = detalle is null ? null : Map(detalle);

            if (orden is null)
                return RedirectToAction(nameof(Index), new { error = "Orden no encontrada o no está asignada a ti." });

            vm.Ordenes.Add(orden);

            return View("Detalle", vm);
        }

        private static MisOrdenesRowVm Map(OrdenPilotoDto o) => new() {
            OrdenId = o.OrdenId, NumeroOrdenA = o.NumeroOrdenA, CodigoA = o.CodigoA, Estado = o.Estado,
            CreatedAt = o.CreatedAt, PuedeVerDestinoFinal = o.PuedeVerDestinoFinal, TiendaNombre = o.TiendaNombre,
            TiendaDireccion = o.TiendaDireccion, TiendaTelefono = o.TiendaTelefono, NotaPedido = o.NotaPedido,
            UsuarioFinalNombre = o.UsuarioFinalNombre, UsuarioFinalDireccion = o.UsuarioFinalDireccion,
            UsuarioFinalTelefono = o.UsuarioFinalTelefono, UsuarioFinalMapaLink = o.UsuarioFinalMapaLink,
            Precio = o.Precio, DistanciaKm = o.DistanciaKm, Paradas = o.Paradas,
            PuedeRecoger = o.PuedeRecoger, BloqueoRecogida = o.BloqueoRecogida };

        // POST: /Piloto/ConfirmarRecolecta  (escaneo/pegar B)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("CodigosEntrega")]
        public async Task<IActionResult> ConfirmarRecolecta(ConfirmarRecolectaVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(Index), new { error = "Código B inválido." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Redirect("/Identity/Account/Login");

            var piloto = await GetPilotoLogueadoAsync(userId);
            if (piloto is null) return Forbid();

            try
            {
                await _ordenService.ConfirmarRecolectaAsync(input.CodigoB, userId, "Piloto", input.OrdenId, input.OperacionId);

                // Buscar la orden para redirigir al detalle (destino final desbloqueado)
                var ordenId = await _db.CodigosEntrega
                    .AsNoTracking()
                    .Where(c => c.Codigo == input.CodigoB && c.Tipo == TipoCodigo.B_Recoleccion
                        && c.OrdenEntrega.PilotoId == piloto.Id && c.OrdenEntrega.ClienteAppId == piloto.ClienteAppId
                        && (input.OrdenId == null || c.OrdenEntregaId == input.OrdenId))
                    .Select(c => c.OrdenEntregaId)
                    .FirstOrDefaultAsync();

                if (ordenId > 0)
                    return RedirectToAction(nameof(Detalle), new { id = ordenId, ok = "Recolecta confirmada ✅ (destino final desbloqueado)" });

                return RedirectToAction(nameof(Index), new { ok = "Recolecta confirmada ✅ (destino final desbloqueado)" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Index), new { error = ex.Message });
            }
        }

        // POST: /Piloto/ConfirmarEntrega (C por parada; B+C sólo para clientes anteriores)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("CodigosEntrega")]
        public async Task<IActionResult> ConfirmarEntrega(ConfirmarEntregaVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(Index), new { error = "Datos inválidos para entrega." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Redirect("/Identity/Account/Login");

            var piloto = await GetPilotoLogueadoAsync(userId);
            if (piloto is null) return Forbid();

            try
            {
                if (input.OrdenId.HasValue && input.ParadaId.HasValue)
                {
                    var result = await _ordenService.ConfirmarEntregaParadaAsync(input.OrdenId.Value, input.ParadaId.Value,
                        input.CodigoC, userId, input.OperacionId);
                    if (!EstadosEntrega.EsTerminal(System.Enum.Parse<EstadoOrden>(result.EstadoOrden)))
                        return RedirectToAction(nameof(Detalle), new { id = result.OrdenId, ok = "Parada entregada" });
                }
                else await _ordenService.ConfirmarEntregaAsync(input.CodigoB, input.CodigoC, userId, "Piloto", input.OrdenId);
                return RedirectToAction(nameof(Index), new { ok = "Entrega confirmada ✅" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Index), new { error = ex.Message });
            }
        }
    }
}
