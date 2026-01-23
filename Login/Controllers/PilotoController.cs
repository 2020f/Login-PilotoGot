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

        // GET: /Piloto/Index
        [HttpGet]
        public async Task<IActionResult> Index(string? ok = null, string? error = null)
        {
            var vm = new MisOrdenesIndexVm { Ok = ok, Error = error };

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Redirect("/Identity/Account/Login");

            // Encontrar el Piloto logueado
            var piloto = await _db.Pilotos.AsNoTracking()
                .SingleOrDefaultAsync(p => p.IdentityUserId == userId && p.Activo);

            if (piloto is null)
                return Forbid(); // logueado pero no es piloto activo

            // Traer órdenes asignadas a él (y no entregadas aún, opcional)
            var ordenes = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.UsuarioFinal)
                .Include(o => o.Codigos)
                .Where(o => o.PilotoId == piloto.Id &&
                            (o.Estado == EstadoOrden.Asignada || o.Estado == EstadoOrden.Recolectada))
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt)
                .Take(200)
                .Select(o => new
                {
                    o.Id,
                    o.NumeroOrdenA,
                    Estado = o.Estado.ToString(),
                    o.CreatedAt,
                    PuedeVer = o.Estado == EstadoOrden.Recolectada,
                    CodigoB = o.Codigos.Where(c => c.Tipo == TipoCodigo.B_Recoleccion)
                                       .Select(c => c.Codigo)
                                       .FirstOrDefault(),
                    UFNombre = o.UsuarioFinal.Nombre,
                    UFTel = o.UsuarioFinal.Telefono,
                    UFDireccion = o.UsuarioFinal.DireccionUbicacion
                })
                .ToListAsync();

            vm.Ordenes = ordenes
                .Where(x => !string.IsNullOrWhiteSpace(x.CodigoB))
                .Select(x => new MisOrdenesRowVm
                {
                    OrdenId = x.Id,
                    NumeroOrdenA = x.NumeroOrdenA,
                    Estado = x.Estado,
                    CreatedAt = x.CreatedAt,
                    CodigoB = x.CodigoB!,
                    PuedeVerDestino = x.PuedeVer,
                    NombreUsuarioFinal = x.PuedeVer ? x.UFNombre : null,
                    TelefonoUsuarioFinal = x.PuedeVer ? x.UFTel : null,
                    DireccionUbicacion = x.PuedeVer ? x.UFDireccion : null
                })
                .ToList();

            return View(vm);
        }

        // POST: /Piloto/ConfirmarRecolecta
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarRecolecta(ConfirmarRecolectaVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(Index), new { error = "Código B inválido." });

            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
            var actorRol = "Piloto";

            try
            {
                await _ordenService.ConfirmarRecolectaAsync(input.CodigoB, actorId, actorRol);
                return RedirectToAction(nameof(Index), new { ok = "Recolecta confirmada ✅" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Index), new { error = ex.Message });
            }
        }

        // POST: /Piloto/ConfirmarEntrega
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarEntrega(ConfirmarEntregaVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(Index), new { error = "Datos inválidos para entrega." });

            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
            var actorRol = "Piloto";

            try
            {
                await _ordenService.ConfirmarEntregaAsync(input.CodigoB, input.CodigoC, actorId, actorRol);
                return RedirectToAction(nameof(Index), new { ok = "Entrega confirmada ✅" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Index), new { error = ex.Message });
            }
        }
    }
}
