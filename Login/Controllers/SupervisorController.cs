using System.Linq;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.ViewModels.Supervisor;
using Login.ViewModels.Tienda;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers
{
    // ✅ Este controller es el "Gestor principal" (Interfaz #1)
    [Authorize(Roles = "Gestor")]
    public class SupervisorController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IOrdenService _ordenService;
        private readonly UserManager<IdentityUser> _userManager;

        public SupervisorController(
            ApplicationDbContext db,
            IOrdenService ordenService,
            UserManager<IdentityUser> userManager)
        {
            _db = db;
            _ordenService = ordenService;
            _userManager = userManager;
        }

        private string GetUserId()
            => User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

        // ✅ Clave tenant: el Gestor está amarrado a 1 ClienteApp por ClienteApp.GestorIdentityUserId
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
        // TIENDAS (Gestor)
        // ==========================
        [HttpGet]
        public async Task<IActionResult> Tiendas(string? ok = null, string? error = null)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Ordenes), new { error = ex.Message }); }

            ViewBag.Ok = ok;
            ViewBag.Error = error;

            var tiendas = await _db.Tiendas
                .AsNoTracking()
                .Where(t => t.ClienteAppId == clienteAppId)
                .OrderByDescending(t => t.Id)
                .ToListAsync();

            return View(tiendas);
        }

        [HttpGet]
        public IActionResult CrearTienda()
        {
            return View(new TiendaCreateVm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearTienda(TiendaCreateVm input)
        {
            if (!ModelState.IsValid)
                return View(input);

            var userId = _userManager.GetUserId(User);

            // ✅ ClienteApp del Gestor (tenant)
            var cliente = await _db.ClientesApp
                .Include(c => c.Plan)
                .SingleAsync(c => c.GestorIdentityUserId == userId);

            // ✅ límite por plan
            var total = await _db.Tiendas.CountAsync(t => t.ClienteAppId == cliente.Id && t.Activo);
            if (total >= cliente.Plan.MaxTiendas)
                return RedirectToAction(nameof(Tiendas), new { error = "Límite de tiendas alcanzado por tu plan." });

            // ✅ validar email no usado
            var email = input.EmailCliente.Trim().ToLower();
            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null)
            {
                ModelState.AddModelError(nameof(input.EmailCliente), "Ese email ya existe. Usa otro.");
                return View(input);
            }

            // ✅ 1) Crear Tienda (BD)
            var tienda = new Tienda
            {
                ClienteAppId = cliente.Id,
                Nombre = input.Nombre.Trim(),
                Direccion = input.Direccion.Trim(),
                Telefono = input.Telefono.Trim(),
                Activo = true
            };

            _db.Tiendas.Add(tienda);
            await _db.SaveChangesAsync(); // para obtener tienda.Id

            // ✅ 2) Crear IdentityUser para esa Tienda (rol Cliente)
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createRes = await _userManager.CreateAsync(user, input.PasswordCliente);
            if (!createRes.Succeeded)
            {
                // rollback simple: borrar la tienda creada si el usuario falla
                _db.Tiendas.Remove(tienda);
                await _db.SaveChangesAsync();

                foreach (var e in createRes.Errors)
                    ModelState.AddModelError("", e.Description);

                return View(input);
            }

            // ✅ 3) asignar rol Cliente
            var roleRes = await _userManager.AddToRoleAsync(user, "Cliente");
            if (!roleRes.Succeeded)
            {
                // rollback: borrar user y tienda si falla
                await _userManager.DeleteAsync(user);
                _db.Tiendas.Remove(tienda);
                await _db.SaveChangesAsync();

                foreach (var e in roleRes.Errors)
                    ModelState.AddModelError("", e.Description);

                return View(input);
            }

            // ✅ 4) vincular usuario ↔ tienda (UsuariosTienda)
            _db.UsuariosTienda.Add(new UsuarioTienda
            {
                IdentityUserId = user.Id,
                TiendaId = tienda.Id,
                Activo = true,
                EsPrincipal = true
            });

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Tiendas), new { ok = "Tienda creada y usuario Cliente generado ✅" });
        }









        // ==========================
        // PILOTOS (Gestor)
        // ==========================
        [HttpGet]
        public async Task<IActionResult> Pilotos(string? ok = null, string? error = null)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Ordenes), new { error = ex.Message }); }

            ViewBag.Ok = ok;
            ViewBag.Error = error;

            var pilotos = await _db.Pilotos
                .AsNoTracking()
                .Where(p => p.ClienteAppId == clienteAppId)
                .OrderByDescending(p => p.Id)
                .ToListAsync();

            return View(pilotos);
        }

        [HttpGet]
        public IActionResult CrearPiloto()
        {
            return View(new Login.ViewModels.Supervisor.PilotoCreateVm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearPiloto(Login.ViewModels.Supervisor.PilotoCreateVm input)
        {
            if (!ModelState.IsValid)
                return View(input);

            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Pilotos), new { error = ex.Message }); }

            // ✅ cargar cliente + plan para límites
            var cliente = await _db.ClientesApp
                .Include(c => c.Plan)
                .SingleAsync(c => c.Id == clienteAppId);

            // ✅ límite por plan (MaxPilotos)
            var totalPilotos = await _db.Pilotos.CountAsync(p => p.ClienteAppId == clienteAppId && p.Activo);
            if (totalPilotos >= cliente.Plan.MaxPilotos)
                return RedirectToAction(nameof(Pilotos), new { error = "Límite de pilotos alcanzado por tu plan." });

            // ✅ validar email no usado
            var email = input.EmailPiloto.Trim().ToLower();
            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null)
            {
                ModelState.AddModelError(nameof(input.EmailPiloto), "Ese email ya existe. Usa otro.");
                return View(input);
            }

            // ✅ 1) crear IdentityUser del piloto
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createRes = await _userManager.CreateAsync(user, input.PasswordPiloto);
            if (!createRes.Succeeded)
            {
                foreach (var e in createRes.Errors)
                    ModelState.AddModelError("", e.Description);

                return View(input);
            }

            // ✅ 2) asignar rol Piloto
            var roleRes = await _userManager.AddToRoleAsync(user, "Piloto");
            if (!roleRes.Succeeded)
            {
                await _userManager.DeleteAsync(user);

                foreach (var e in roleRes.Errors)
                    ModelState.AddModelError("", e.Description);

                return View(input);
            }

            // ✅ 3) crear Piloto en BD ligado al tenant + amarrado a IdentityUserId
            var piloto = new Piloto
            {
                ClienteAppId = clienteAppId,
                Nombre = input.Nombre.Trim(),
                Telefono = input.Telefono.Trim(),
                Estado = EstadoPiloto.Disponible,
                IdentityUserId = user.Id,
                Activo = true
            };

            _db.Pilotos.Add(piloto);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Pilotos), new { ok = "Piloto creado y usuario generado ✅" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DesactivarPiloto(int id)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Pilotos), new { error = ex.Message }); }

            var piloto = await _db.Pilotos.SingleOrDefaultAsync(p => p.Id == id && p.ClienteAppId == clienteAppId);
            if (piloto is null)
                return RedirectToAction(nameof(Pilotos), new { error = "Piloto no encontrado." });

            piloto.Activo = false;
            piloto.Estado = EstadoPiloto.Inactivo;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Pilotos), new { ok = "Piloto desactivado ✅" });
        }













































        // ==========================
        // ÓRDENES (Gestor)
        // ==========================
        [HttpGet]
        public async Task<IActionResult> Ordenes(int? tiendaId, int? pilotoId, string? estado, int? numeroOrdenA, string? ok = null, string? error = null)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction("Clientes", "SuperAdmin", new { error = ex.Message }); }

            var vm = new OrdenesIndexVm
            {
                TiendaId = tiendaId,
                PilotoId = pilotoId,
                Estado = estado,
                NumeroOrdenA = numeroOrdenA,
                Ok = ok,
                Error = error
            };

            vm.Tiendas = await _db.Tiendas
                .Where(t => t.ClienteAppId == clienteAppId)
                .OrderBy(t => t.Nombre)
                .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Nombre })
                .ToListAsync();

            vm.Pilotos = await _db.Pilotos
                .Where(p => p.ClienteAppId == clienteAppId)
                .OrderBy(p => p.Nombre)
                .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Nombre })
                .ToListAsync();

            vm.Estados = new()
            {
                new SelectListItem { Value = "", Text = "Todos" },
                new SelectListItem { Value = EstadoOrden.Creada.ToString(), Text = "Creada" },
                new SelectListItem { Value = EstadoOrden.Asignada.ToString(), Text = "Asignada" },
                new SelectListItem { Value = EstadoOrden.Recolectada.ToString(), Text = "Recolectada" },
                new SelectListItem { Value = EstadoOrden.Entregada.ToString(), Text = "Entregada" },
                new SelectListItem { Value = EstadoOrden.Incidente.ToString(), Text = "Incidente" },
            };

            var q = _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Tienda)
                .Include(o => o.Piloto)
                .Include(o => o.Codigos)
                .AsQueryable();

            // ✅ SIEMPRE filtrar por tenant
            q = q.Where(o => o.ClienteAppId == clienteAppId);

            if (tiendaId.HasValue) q = q.Where(o => o.TiendaId == tiendaId.Value);
            if (pilotoId.HasValue) q = q.Where(o => o.PilotoId == pilotoId.Value);
            if (numeroOrdenA.HasValue) q = q.Where(o => o.NumeroOrdenA == numeroOrdenA.Value);

            if (!string.IsNullOrWhiteSpace(estado) && System.Enum.TryParse<EstadoOrden>(estado, out var est))
                q = q.Where(o => o.Estado == est);

            vm.Ordenes = await q
                .OrderByDescending(o => o.CreatedAt)
                .Take(200)
                .Select(o => new OrdenRowVm
                {
                    OrdenId = o.Id,
                    ClienteAppId = o.ClienteAppId,
                    TiendaId = o.TiendaId,
                    TiendaNombre = o.Tienda.Nombre,
                    NumeroOrdenA = o.NumeroOrdenA,
                    Estado = o.Estado.ToString(),
                    PilotoId = o.PilotoId,
                    PilotoNombre = o.Piloto != null ? o.Piloto.Nombre : null,
                    CreatedAt = o.CreatedAt,
                    CodigoB = o.Codigos
                        .Where(c => c.Tipo == TipoCodigo.B_Recoleccion)
                        .Select(c => c.Codigo)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return View(vm);
        }

        // POST: /Supervisor/AsignarPiloto
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarPiloto(AsignarPilotoVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(Ordenes), new { error = "Datos inválidos para asignar." });

            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Ordenes), new { error = ex.Message }); }

            // ✅ Seguridad extra: el Gestor SOLO puede asignar órdenes de SU ClienteApp
            var ordenTenant = await _db.CodigosEntrega
                .AsNoTracking()
                .Where(c => c.Codigo == input.CodigoB && c.Tipo == TipoCodigo.B_Recoleccion)
                .Select(c => c.OrdenEntrega.ClienteAppId)
                .SingleOrDefaultAsync();

            if (ordenTenant == 0)
                return RedirectToAction(nameof(Ordenes), new { error = "Código B inválido." });

            if (ordenTenant != clienteAppId)
                return RedirectToAction(nameof(Ordenes), new { error = "No tienes permiso para asignar esa orden." });

            var actorId = User?.FindFirst("sub")?.Value
                       ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? "System";

            var actorRol = "Gestor";

            try
            {
                await _ordenService.AsignarPilotoAsync(input.CodigoB, input.PilotoId, actorId, actorRol);
                return RedirectToAction(nameof(Ordenes), new { ok = "Piloto asignado ✅" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Ordenes), new { error = ex.Message });
            }
        }
    }
}
