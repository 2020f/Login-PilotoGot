using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Login.Data;
using Login.Domain.Entities;
using Login.ViewModels.Gestor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers
{
    [Authorize(Roles = "Gestor")]
    public class GestorController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public GestorController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        private string GetUserId()
            => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        // ✅ clave: saber a qué ClienteApp pertenece este Gestor
        private async Task<int> GetClienteAppIdDelGestorAsync()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
                throw new System.InvalidOperationException("No autenticado.");

            var clienteAppId = await _db.ClientesApp
                .AsNoTracking()
                .Where(c => c.GestorIdentityUserId == userId)
                .Select(c => c.Id)
                .SingleOrDefaultAsync();

            if (clienteAppId == 0)
                throw new System.InvalidOperationException("Este gestor no está asignado a ningún ClienteApp.");

            return clienteAppId;
        }






        // ==========================
        // TIENDAS (crear mínimo)
        // ==========================

        [HttpGet]
        public async Task<IActionResult> CrearTienda(string? error = null)
        {
            ViewBag.Error = error;
            return View(new TiendaCreateVm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearTienda(TiendaCreateVm input)
        {
            if (!ModelState.IsValid)
                return View(input);

            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(UsuariosTienda), new { error = ex.Message }); }

            // ✅ validar límite por plan (MaxTiendas)
            var plan = await _db.ClientesApp
                .Where(c => c.Id == clienteAppId)
                .Select(c => new { c.Plan.MaxTiendas })
                .SingleAsync();

            var tiendasActuales = await _db.Tiendas.CountAsync(t => t.ClienteAppId == clienteAppId && t.Activo);
            if (tiendasActuales >= plan.MaxTiendas)
                return RedirectToAction(nameof(CrearTienda), new { error = "Límite de tiendas alcanzado según el plan." });

            // ✅ crear tienda dentro del tenant
            var tienda = new Tienda
            {
                ClienteAppId = clienteAppId,
                Nombre = input.Nombre.Trim(),
                Direccion = input.Direccion.Trim(),
                Telefono = input.Telefono.Trim(),
                Activo = true
            };

            _db.Tiendas.Add(tienda);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(ListarTiendas), new { ok = "Tienda creada ✅" });
        }

        [HttpGet]
        public async Task<IActionResult> ListarTiendas(string? ok = null, string? error = null)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(UsuariosTienda), new { error = ex.Message }); }

            ViewBag.Ok = ok;
            ViewBag.Error = error;

            var tiendas = await _db.Tiendas.AsNoTracking()
                .Where(t => t.ClienteAppId == clienteAppId)
                .OrderByDescending(t => t.Id)
                .ToListAsync();

            return View(tiendas);
        }


























        // GET: /Gestor/AsignarTiendaUsuario
        [HttpGet]
        public async Task<IActionResult> AsignarTiendaUsuario(string? ok = null, string? error = null)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(UsuariosTienda), new { error = ex.Message }); }

            // ✅ SOLO tiendas de este ClienteApp
            var tiendas = await _db.Tiendas.AsNoTracking()
                .Where(t => t.Activo && t.ClienteAppId == clienteAppId)
                .OrderBy(t => t.Nombre)
                .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Nombre })
                .ToListAsync();

            var vm = new AsignarTiendaUsuarioVm
            {
                Tiendas = tiendas,
                Ok = ok,
                Error = error
            };

            return View(vm);
        }

        // POST: /Gestor/AsignarTiendaUsuario
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarTiendaUsuario(AsignarTiendaUsuarioVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(AsignarTiendaUsuario), new { error = "Datos inválidos." });

            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(AsignarTiendaUsuario), new { error = ex.Message }); }

            // ✅ validar tienda Y que pertenezca a ESTE ClienteApp
            var tienda = await _db.Tiendas.SingleOrDefaultAsync(t =>
                t.Id == input.TiendaId &&
                t.Activo &&
                t.ClienteAppId == clienteAppId);

            if (tienda is null)
                return RedirectToAction(nameof(AsignarTiendaUsuario), new { error = "Tienda inválida o no pertenece a tu ClienteApp." });

            // 2) buscar usuario por email
            var email = input.EmailUsuario.Trim().ToLower();
            var user = await _userManager.FindByEmailAsync(email);
            if (user is null)
                return RedirectToAction(nameof(AsignarTiendaUsuario), new { error = "Usuario no existe (email)." });

            // 3) evitar duplicado
            var existe = await _db.UsuariosTienda.AnyAsync(x => x.IdentityUserId == user.Id && x.TiendaId == tienda.Id);
            if (existe)
                return RedirectToAction(nameof(AsignarTiendaUsuario), new { error = "Ese usuario ya está asignado a esa tienda." });

            // 4) guardar
            _db.UsuariosTienda.Add(new UsuarioTienda
            {
                IdentityUserId = user.Id,
                TiendaId = tienda.Id,
                Activo = true,
                EsPrincipal = false
            });

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(AsignarTiendaUsuario), new { ok = "Asignación creada ✅" });
        }

        // GET: /Gestor/UsuariosTienda
        [HttpGet]
        public async Task<IActionResult> UsuariosTienda(string? ok = null, string? error = null)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction("Index", "Home", new { error = ex.Message }); }

            // ✅ mostrar SOLO asignaciones de tiendas del ClienteApp del gestor
            var asignaciones = await (
                from ut in _db.UsuariosTienda.AsNoTracking()
                join u in _db.Users.AsNoTracking() on ut.IdentityUserId equals u.Id
                join t in _db.Tiendas.AsNoTracking() on ut.TiendaId equals t.Id
                where t.ClienteAppId == clienteAppId
                orderby ut.Id descending
                select new UsuarioTiendaRowVm
                {
                    Id = ut.Id,
                    EmailUsuario = u.Email ?? "(sin email)",
                    TiendaId = t.Id,
                    TiendaNombre = t.Nombre,
                    Activo = ut.Activo
                }
            ).Take(300).ToListAsync();

            var vm = new UsuariosTiendaIndexVm
            {
                Ok = ok,
                Error = error,
                Asignaciones = asignaciones
            };

            return View(vm);
        }
    }
}
