using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Enums;
using Login.ViewModels.Piloto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers
{
    [Authorize(Roles = "Piloto")]
    public class PilotoController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IOrdenService _ordenService;

        public PilotoController(ApplicationDbContext db, IOrdenService ordenService)
        {
            _db = db;
            _ordenService = ordenService;
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

            // ✅ Solo órdenes del piloto, y solo las activas del flujo
            // Tú dijiste que el piloto “solo visualiza una orden”, así que tomamos 1.
            var orden = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Tienda)
                .Include(o => o.UsuarioFinal)
                .Include(o => o.Codigos)
                .Where(o => o.PilotoId == piloto.Id &&
                            (o.Estado == EstadoOrden.Asignada || o.Estado == EstadoOrden.Recolectada))
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt)
                .Select(o => new MisOrdenesRowVm
                {
                    OrdenId = o.Id,
                    NumeroOrdenA = o.NumeroOrdenA,
                    Estado = o.Estado.ToString(),
                    CreatedAt = o.CreatedAt,

                    PuedeVerDestinoFinal = o.Estado == EstadoOrden.Recolectada,

                    // TIENDA (pickup)
                    TiendaNombre = o.Tienda.Nombre,
                    TiendaDireccion = o.Tienda.Direccion,
                    TiendaTelefono = o.Tienda.Telefono,
                    NotaPedido = o.NotaPedido,

                    // USUARIO FINAL (delivery) - solo se mostrará en vista cuando PuedeVerDestinoFinal = true
                    UsuarioFinalNombre = o.UsuarioFinal.Nombre,
                    UsuarioFinalDireccion = o.UsuarioFinal.DireccionUbicacion,
                    UsuarioFinalTelefono = o.UsuarioFinal.Telefono
                })
                .FirstOrDefaultAsync();

            if (orden != null)
                vm.Ordenes.Add(orden);

            return View(vm);
        }

        // POST: /Piloto/ConfirmarRecolecta  (escaneo/pegar B)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarRecolecta(ConfirmarRecolectaVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(Index), new { error = "Código B inválido." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Redirect("/Identity/Account/Login");

            var piloto = await GetPilotoLogueadoAsync(userId);
            if (piloto is null) return Forbid();

            // ✅ Validación dura: el B debe pertenecer a una orden ASIGNADA de este piloto
            var ordenValida = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Codigos)
                .AnyAsync(o =>
                    o.PilotoId == piloto.Id &&
                    o.Estado == EstadoOrden.Asignada &&
                    o.Codigos.Any(c => c.Tipo == TipoCodigo.B_Recoleccion && c.Codigo == input.CodigoB));

            if (!ordenValida)
                return RedirectToAction(nameof(Index), new { error = "Ese B no corresponde a una orden asignada a ti." });

            try
            {
                await _ordenService.ConfirmarRecolectaAsync(input.CodigoB, userId, "Piloto");
                return RedirectToAction(nameof(Index), new { ok = "Recolecta confirmada ✅ (destino final desbloqueado)" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Index), new { error = ex.Message });
            }
        }

        // POST: /Piloto/ConfirmarEntrega  (C + escaneo/pegar B otra vez)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarEntrega(ConfirmarEntregaVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(Index), new { error = "Datos inválidos para entrega." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Redirect("/Identity/Account/Login");

            var piloto = await GetPilotoLogueadoAsync(userId);
            if (piloto is null) return Forbid();

            // ✅ Validación dura: debe ser su orden y estar RECOLECTADA
            var ordenValida = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Codigos)
                .AnyAsync(o =>
                    o.PilotoId == piloto.Id &&
                    o.Estado == EstadoOrden.Recolectada &&
                    o.Codigos.Any(c => c.Tipo == TipoCodigo.B_Recoleccion && c.Codigo == input.CodigoB));

            if (!ordenValida)
                return RedirectToAction(nameof(Index), new { error = "No puedes cerrar: la orden no está recolectada o no es tuya." });

            try
            {
                await _ordenService.ConfirmarEntregaAsync(input.CodigoB, input.CodigoC, userId, "Piloto");
                return RedirectToAction(nameof(Index), new { ok = "Entrega confirmada ✅" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Index), new { error = ex.Message });
            }
        }
    }
}
