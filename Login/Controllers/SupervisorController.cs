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



        [HttpGet]
        public async Task<IActionResult> EditarTienda(int id, string? ok = null, string? error = null)
        {

            ViewBag.Ok = ok;
            ViewBag.Error = error;


            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Tiendas), new { error = ex.Message }); }

            var tienda = await _db.Tiendas
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == id && t.ClienteAppId == clienteAppId);

            if (tienda is null)
                return RedirectToAction(nameof(Tiendas), new { error = "Tienda no encontrada." });

            // Buscar email del usuario principal de esa tienda (UsuariosTienda -> IdentityUser)
            var identityId = await _db.UsuariosTienda
                .AsNoTracking()
                .Where(ut => ut.TiendaId == tienda.Id && ut.Activo && ut.EsPrincipal)
                .Select(ut => ut.IdentityUserId)
                .FirstOrDefaultAsync();

            string? email = null;
            if (!string.IsNullOrWhiteSpace(identityId))
            {
                var user = await _userManager.FindByIdAsync(identityId);
                email = user?.Email;
            }

            var vm = new Login.ViewModels.Tienda.TiendaEditVm
            {
                Id = tienda.Id,
                Nombre = tienda.Nombre,
                Direccion = tienda.Direccion,
                Telefono = tienda.Telefono,
                Activo = tienda.Activo,
                EmailCliente = email
            };

            return View(vm);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarTienda(Login.ViewModels.Tienda.TiendaEditVm input)
        {
            if (!ModelState.IsValid)
                return View(input);

            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Tiendas), new { error = ex.Message }); }

            var tienda = await _db.Tiendas
                .SingleOrDefaultAsync(t => t.Id == input.Id && t.ClienteAppId == clienteAppId);

            if (tienda is null)
                return RedirectToAction(nameof(Tiendas), new { error = "Tienda no encontrada." });

            tienda.Nombre = input.Nombre.Trim();
            tienda.Direccion = input.Direccion.Trim();
            tienda.Telefono = input.Telefono.Trim();
            tienda.Activo = input.Activo;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Tiendas), new { ok = "Tienda actualizada ✅" });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DesactivarTienda(int id)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Tiendas), new { error = ex.Message }); }

            var tienda = await _db.Tiendas
                .SingleOrDefaultAsync(t => t.Id == id && t.ClienteAppId == clienteAppId);

            if (tienda is null)
                return RedirectToAction(nameof(Tiendas), new { error = "Tienda no encontrada." });

            // Bloqueo si tiene órdenes pendientes
            var tienePendientes = await _db.OrdenesEntrega
                .AnyAsync(o => o.TiendaId == tienda.Id &&
                               (o.Estado == EstadoOrden.Creada ||
                                o.Estado == EstadoOrden.Asignada ||
                                o.Estado == EstadoOrden.Recolectada));

            if (tienePendientes)
                return RedirectToAction(nameof(Tiendas), new { error = "No puedes desactivar: la tienda tiene órdenes activas." });

            tienda.Activo = false;

            // (Opcional) También desactivar el usuario principal de la tienda en Identity
            var identityId = await _db.UsuariosTienda
                .Where(ut => ut.TiendaId == tienda.Id && ut.Activo && ut.EsPrincipal)
                .Select(ut => ut.IdentityUserId)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(identityId))
            {
                var user = await _userManager.FindByIdAsync(identityId);
                if (user != null)
                {
                    user.LockoutEnabled = true;
                    user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100); // “bloqueado por la eternidad”
                    await _userManager.UpdateAsync(user);
                }
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Tiendas), new { ok = "Tienda desactivada ✅" });
        }




        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPasswordTienda(Login.ViewModels.Tienda.ResetPasswordTiendaVm input)
        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = "Password inválido." });

            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Tiendas), new { error = ex.Message });
            }

            // ✅ Seguridad: la tienda debe ser del tenant del Gestor
            var tiendaOk = await _db.Tiendas
                .AsNoTracking()
                .AnyAsync(t => t.Id == input.TiendaId && t.ClienteAppId == clienteAppId);

            if (!tiendaOk)
                return RedirectToAction(nameof(Tiendas), new { error = "No tienes permiso para resetear esa tienda." });

            // ✅ Buscar IdentityUserId del usuario principal de esa tienda
            var identityId = await _db.UsuariosTienda
                .AsNoTracking()
                .Where(ut => ut.TiendaId == input.TiendaId && ut.Activo && ut.EsPrincipal)
                .Select(ut => ut.IdentityUserId)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(identityId))
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = "La tienda no tiene usuario principal." });

            var user = await _userManager.FindByIdAsync(identityId);
            if (user is null)
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = "Usuario de la tienda no existe en Identity." });

            // ✅ Reset real de password (sin saber el actual)
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetRes = await _userManager.ResetPasswordAsync(user, token, input.NuevoPassword);

            if (!resetRes.Succeeded)
            {
                var msg = string.Join(" | ", resetRes.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = msg });
            }

            // ✅ Si estaba lockeado, lo desbloqueamos (por si desactivaste antes)
            user.LockoutEnd = null;
            await _userManager.UpdateAsync(user);

            return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, ok = "Password reseteado ✅" });
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






        [HttpGet]
        public async Task<IActionResult> EditarPiloto(int id)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Pilotos), new { error = ex.Message }); }

            var piloto = await _db.Pilotos
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == id && p.ClienteAppId == clienteAppId);

            if (piloto is null)
                return RedirectToAction(nameof(Pilotos), new { error = "Piloto no encontrado." });

            var user = await _userManager.FindByIdAsync(piloto.IdentityUserId);

            var vm = new Login.ViewModels.Supervisor.PilotoEditVm
            {
                Id = piloto.Id,
                Nombre = piloto.Nombre,
                Telefono = piloto.Telefono,
                Estado = piloto.Estado,
                Activo = piloto.Activo,
                EmailPiloto = user?.Email
            };

            return View(vm);
        }







        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarPiloto(Login.ViewModels.Supervisor.PilotoEditVm input)
        {
            if (!ModelState.IsValid)
                return View(input);

            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Pilotos), new { error = ex.Message }); }

            var piloto = await _db.Pilotos
                .SingleOrDefaultAsync(p => p.Id == input.Id && p.ClienteAppId == clienteAppId);

            if (piloto is null)
                return RedirectToAction(nameof(Pilotos), new { error = "Piloto no encontrado." });

            piloto.Nombre = input.Nombre.Trim();
            piloto.Telefono = input.Telefono.Trim();
            piloto.Estado = input.Estado;
            piloto.Activo = input.Activo;

            // Si lo desactivan desde editar, forzamos estado Inactivo
            if (!piloto.Activo)
                piloto.Estado = EstadoPiloto.Inactivo;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Pilotos), new { ok = "Piloto actualizado ✅" });
        }







        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarPiloto(int id)
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
            return RedirectToAction(nameof(Pilotos), new { ok = "Piloto eliminado (desactivado) ✅" });
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
