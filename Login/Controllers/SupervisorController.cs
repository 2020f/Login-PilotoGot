using System.Linq;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
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
        private readonly IGoogleMapsService _googleMaps;
        private readonly QuotaPolicyService _quotas;

        public SupervisorController(
            ApplicationDbContext db,
            IOrdenService ordenService,
            UserManager<IdentityUser> userManager,
            IGoogleMapsService googleMaps,
            QuotaPolicyService quotas)
        {
            _db = db;
            _ordenService = ordenService;
            _userManager = userManager;
            _googleMaps = googleMaps;
            _quotas = quotas;
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
            if (!ModelState.IsValid) return View(input);
            try
            {
                var tenantId = await GetClienteAppIdDelGestorAsync();
                var coords = await _googleMaps.GeocodificarAsync(input.Direccion.Trim());
                await using var tx = await _quotas.BeginTenantMutationAsync(tenantId);
                await _quotas.EnsureNewStoreAsync(tenantId, createsUser: true);
                var email = input.EmailCliente.Trim().ToLowerInvariant();
                if (await _userManager.FindByEmailAsync(email) != null) throw new InvalidOperationException("Ese email ya existe. Usa otro.");
                var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                var created = await _userManager.CreateAsync(user, input.PasswordCliente);
                if (!created.Succeeded) throw new InvalidOperationException(string.Join(" | ", created.Errors.Select(e => e.Description)));
                var role = await _userManager.AddToRoleAsync(user, "Cliente");
                if (!role.Succeeded) throw new InvalidOperationException(string.Join(" | ", role.Errors.Select(e => e.Description)));
                var tienda = new Tienda { ClienteAppId = tenantId, Nombre = input.Nombre.Trim(), Direccion = input.Direccion.Trim(),
                    Telefono = input.Telefono.Trim(), Latitud = coords?.Lat, Longitud = coords?.Lng, Activo = true };
                _db.UsuariosTienda.Add(new UsuarioTienda { Tienda = tienda, IdentityUserId = user.Id, Activo = true, EsPrincipal = true });
                await _db.SaveChangesAsync(); await tx.CommitAsync();
                return RedirectToAction(nameof(Tiendas), new { ok = "Comercio y acceso creados." });
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); return View(input); }
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
            if (!ModelState.IsValid) return View(input);
            var tenantId = await GetClienteAppIdDelGestorAsync();
            try
            {
                var coords = await _googleMaps.GeocodificarAsync(input.Direccion.Trim());
                await using var tx = await _quotas.BeginTenantMutationAsync(tenantId);
                var tienda = await _db.Tiendas.SingleOrDefaultAsync(t => t.Id == input.Id && t.ClienteAppId == tenantId);
                if (tienda == null) return NotFound();
                if (!input.Activo) await _quotas.EnsureNoOpenStoreOrdersAsync(tenantId, tienda.Id);
                else if (!tienda.Activo) await _quotas.EnsureStoreReactivationAsync(tenantId, tienda.Id);
                tienda.Nombre = input.Nombre.Trim(); tienda.Direccion = input.Direccion.Trim();
                tienda.Telefono = input.Telefono.Trim(); tienda.Activo = input.Activo;
                if (coords != null) { tienda.Latitud = coords.Value.Lat; tienda.Longitud = coords.Value.Lng; }
                await _db.SaveChangesAsync(); await tx.CommitAsync();
                return RedirectToAction(nameof(Tiendas), new { ok = "Comercio actualizado." });
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); return View(input); }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DesactivarTienda(int id)
        {
            var tenantId = await GetClienteAppIdDelGestorAsync();
            try
            {
                await using var tx = await _quotas.BeginTenantMutationAsync(tenantId);
                var tienda = await _db.Tiendas.SingleOrDefaultAsync(t => t.Id == id && t.ClienteAppId == tenantId);
                if (tienda == null) return NotFound();
                await _quotas.EnsureNoOpenStoreOrdersAsync(tenantId, id);
                tienda.Activo = false;
                // No bloquear la identidad: puede tener acceso a otros comercios.
                await _db.SaveChangesAsync(); await tx.CommitAsync();
                return RedirectToAction(nameof(Tiendas), new { ok = "Comercio desactivado." });
            }
            catch (InvalidOperationException ex) { return RedirectToAction(nameof(Tiendas), new { error = ex.Message }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAutoAsignacion(int id)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Tiendas), new { error = ex.Message }); }

            var tienda = await _db.Tiendas
                .SingleOrDefaultAsync(t => t.Id == id && t.ClienteAppId == clienteAppId);

            if (tienda is null)
                return RedirectToAction(nameof(Tiendas), new { error = "Tienda no encontrada." });

            tienda.AutoAsignacion = !tienda.AutoAsignacion;
            await _db.SaveChangesAsync();

            var estado = tienda.AutoAsignacion ? "activado" : "desactivado";
            return RedirectToAction(nameof(Tiendas), new { ok = $"Piloto automático {estado} para {tienda.Nombre} ✅" });
        }




        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPasswordTienda(Login.ViewModels.Tienda.ResetPasswordTiendaVm input)        {
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = "Password inválido." });

            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Tiendas), new { error = ex.Message });
            }

            // La credencial pertenece a Identity, no a una tienda. SERIALIZABLE
            // protege también el conjunto de pertenencias/roles durante el reset:
            // otra empresa no puede vincular esta identidad entre la guarda y el commit.
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var tenantDisponible = await _db.ClientesApp
                .Where(c => c.Id == clienteAppId && c.GestorIdentityUserId == GetUserId()
                    && c.Estado == EstadoClienteApp.Activo)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrdenSeq, c => c.OrdenSeq));
            if (tenantDisponible != 1)
                return Forbid();

            // La tienda debe ser del tenant del gestor.
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

            // Tomar el lock de escritura antes de leer roles/pertenencias evita
            // convertir un lock compartido de Identity en exclusivo al cambiar el sello.
            var identityDisponible = await _db.Users.Where(u => u.Id == identityId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ConcurrencyStamp, u => u.ConcurrencyStamp));
            if (identityDisponible != 1)
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = "Usuario de la tienda no existe en Identity." });

            var user = await _userManager.FindByIdAsync(identityId);
            if (user is null)
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = "Usuario de la tienda no existe en Identity." });

            var roles = await _userManager.GetRolesAsync(user);
            var otraEmpresa = await _db.UsuariosTienda.AsNoTracking()
                .AnyAsync(ut => ut.IdentityUserId == identityId && ut.Tienda.ClienteAppId != clienteAppId);
            var otroPerfil = roles.Count != 1 || !roles.Contains("Cliente")
                || await _db.Pilotos.AsNoTracking().AnyAsync(p => p.IdentityUserId == identityId)
                || await _db.ClientesApp.AsNoTracking().AnyAsync(c => c.GestorIdentityUserId == identityId);
            if (otraEmpresa || otroPerfil)
                return RedirectToAction(nameof(EditarTienda), new {
                    id = input.TiendaId,
                    error = "Esta identidad tiene accesos compartidos. Su titular debe recuperar la contraseña desde la pantalla de acceso. El gestor sólo administra los accesos de su empresa."
                });

            // Sólo una identidad comercial exclusiva de esta empresa puede ser
            // administrada aquí. El reset invalida sus sesiones mediante SecurityStamp.
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetRes = await _userManager.ResetPasswordAsync(user, token, input.NuevoPassword);

            if (!resetRes.Succeeded)
            {
                var msg = string.Join(" | ", resetRes.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = msg });
            }

            // ✅ Si estaba lockeado, lo desbloqueamos (por si desactivaste antes)
            user.LockoutEnd = null;
            var updateRes = await _userManager.UpdateAsync(user);
            if (!updateRes.Succeeded)
                return RedirectToAction(nameof(EditarTienda), new { id = input.TiendaId, error = "No se pudo completar el cambio de contraseña. Inténtalo de nuevo." });

            await tx.CommitAsync();

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

            var pilotos = await _db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == clienteAppId).OrderBy(p => p.Nombre)
                .Select(p => new PilotOperationsVm { Id = p.Id, Nombre = p.Nombre, Telefono = p.Telefono, Activo = p.Activo,
                    EnLinea = p.EnLinea, OrdenesActivas = p.Ordenes.Count(o => !EstadosEntrega.Terminales.Contains(o.Estado)) }).ToListAsync();

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
            if (!ModelState.IsValid) return View(input);
            try
            {
                var tenantId = await GetClienteAppIdDelGestorAsync();
                await using var tx = await _quotas.BeginTenantMutationAsync(tenantId);
                await _quotas.EnsureNewPilotAsync(tenantId);
                var email = input.EmailPiloto.Trim().ToLowerInvariant();
                if (await _userManager.FindByEmailAsync(email) != null) throw new InvalidOperationException("Ese email ya existe. Usa otro.");
                var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                var created = await _userManager.CreateAsync(user, input.PasswordPiloto);
                if (!created.Succeeded) throw new InvalidOperationException(string.Join(" | ", created.Errors.Select(e => e.Description)));
                var role = await _userManager.AddToRoleAsync(user, "Piloto");
                if (!role.Succeeded) throw new InvalidOperationException(string.Join(" | ", role.Errors.Select(e => e.Description)));
                _db.Pilotos.Add(new Piloto { ClienteAppId = tenantId, Nombre = input.Nombre.Trim(), Telefono = input.Telefono.Trim(),
                    Estado = EstadoPiloto.Disponible, IdentityUserId = user.Id, Activo = true });
                await _db.SaveChangesAsync(); await tx.CommitAsync();
                return RedirectToAction(nameof(Pilotos), new { ok = "Piloto y acceso creados." });
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); return View(input); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DesactivarPiloto(int id)
        { return await CambiarBajaPilotoAsync(id); }






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
                EmailPiloto = user?.Email ?? string.Empty
            };

            return await RenderPilotoEditAsync(vm, piloto, user);
        }







        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarPiloto(Login.ViewModels.Supervisor.PilotoEditVm input)
        {
            var tenantId = await GetClienteAppIdDelGestorAsync();
            Piloto? piloto = null;
            IdentityUser? user = null;
            try
            {
                // Identity es global. Bloquear primero empresa e identidad y
                // comprobar sus vínculos dentro de la misma transacción.
                await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var tenantDisponible = await _db.ClientesApp
                    .Where(c => c.Id == tenantId && c.GestorIdentityUserId == GetUserId()
                        && c.Estado == EstadoClienteApp.Activo)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrdenSeq, c => c.OrdenSeq));
                if (tenantDisponible != 1) return Forbid();
                piloto = await _db.Pilotos.SingleOrDefaultAsync(p => p.Id == input.Id && p.ClienteAppId == tenantId);
                if (piloto == null) return NotFound();
                var identityDisponible = await _db.Users.Where(u => u.Id == piloto.IdentityUserId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.ConcurrencyStamp, u => u.ConcurrencyStamp));
                user = identityDisponible == 1 ? await _userManager.FindByIdAsync(piloto.IdentityUserId) : null;
                if (!ModelState.IsValid) return await RenderPilotoEditAsync(input, piloto, user);

                var email = input.EmailPiloto.ToLowerInvariant();
                var cambiaCorreo = !string.Equals(user?.Email, email, StringComparison.OrdinalIgnoreCase);
                var cambiaPassword = !string.IsNullOrEmpty(input.NuevoPassword);
                var cambiaAcceso = cambiaCorreo || cambiaPassword;
                if (cambiaAcceso && !await PuedeAdministrarAccesoPilotoAsync(piloto, user))
                {
                    ModelState.AddModelError("", AccesoPilotoCompartido);
                    return await RenderPilotoEditAsync(input, piloto, user);
                }
                if (!input.Activo) await _quotas.EnsureNoOpenPilotOrdersAsync(tenantId, piloto.Id);
                else if (!piloto.Activo) await _quotas.EnsurePilotReactivationAsync(tenantId, piloto.Id, piloto.IdentityUserId);
                var ocupadas = await _db.OrdenesEntrega.AnyAsync(o => o.ClienteAppId == tenantId && o.PilotoId == piloto.Id && !EstadosEntrega.Terminales.Contains(o.Estado));

                if (cambiaCorreo)
                {
                    // El login admite UserName además de Email: cambiar ambos
                    // evita que el correo anterior siga sirviendo como alias.
                    EnsurePilotIdentityUpdate(await _userManager.SetUserNameAsync(user!, email));
                    EnsurePilotIdentityUpdate(await _userManager.SetEmailAsync(user!, email));
                }
                if (cambiaPassword)
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user!);
                    EnsurePilotIdentityUpdate(await _userManager.ResetPasswordAsync(user!, token, input.NuevoPassword!));
                }
                piloto.Nombre = input.Nombre.Trim(); piloto.Telefono = input.Telefono.Trim(); piloto.Activo = input.Activo;
                piloto.Estado = !input.Activo ? EstadoPiloto.Inactivo : ocupadas ? EstadoPiloto.Ocupado : EstadoPiloto.Disponible;
                if (!input.Activo) piloto.EnLinea = false;
                await _db.SaveChangesAsync(); await tx.CommitAsync();
                return RedirectToAction(nameof(Pilotos), new { ok = cambiaAcceso
                    ? "Piloto y acceso actualizados. El piloto debe volver a iniciar sesión en la app."
                    : "Piloto actualizado." });
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
            catch (DbUpdateException)
            {
                ModelState.AddModelError("", "No se pudieron guardar los cambios. El correo puede estar en uso o los datos cambiaron. Recarga la página e inténtalo de nuevo.");
            }
            if (piloto is null)
                return RedirectToAction(nameof(Pilotos), new { error = "No se pudo guardar el piloto. Recarga la página e inténtalo de nuevo." });
            return await RenderPilotoEditAsync(input, piloto, user);
        }

        private const string AccesoPilotoCompartido = "Esta cuenta tiene accesos compartidos o un perfil administrativo. Solo puedes cambiar las credenciales de un piloto exclusivo de tu empresa. Su titular puede recuperar la contraseña desde la pantalla de acceso.";

        private async Task<bool> PuedeAdministrarAccesoPilotoAsync(Piloto piloto, IdentityUser? user)
        {
            if (user is null) return false;
            var roles = await _userManager.GetRolesAsync(user);
            return roles.Count == 1 && roles.Contains("Piloto")
                && !await _db.Pilotos.AnyAsync(p => p.IdentityUserId == user.Id && p.Id != piloto.Id)
                && !await _db.UsuariosTienda.AnyAsync(u => u.IdentityUserId == user.Id)
                && !await _db.UsuariosClienteApp.AnyAsync(u => u.IdentityUserId == user.Id)
                && !await _db.ClientesApp.AnyAsync(c => c.GestorIdentityUserId == user.Id);
        }

        private static void EnsurePilotIdentityUpdate(IdentityResult result)
        {
            if (result.Succeeded) return;
            if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
                throw new InvalidOperationException("Ese correo ya está en uso. Usa otro.");
            throw new InvalidOperationException("No se pudo actualizar el acceso del piloto. Revisa el correo y los requisitos de la nueva contraseña.");
        }

        private async Task<IActionResult> RenderPilotoEditAsync(PilotoEditVm input, Piloto piloto, IdentityUser? user)
        {
            Response.Headers.CacheControl = "no-store";
            input.Estado = piloto.Estado;
            input.PuedeEditarAcceso = await PuedeAdministrarAccesoPilotoAsync(piloto, user);
            input.MotivoAccesoBloqueado = input.PuedeEditarAcceso ? null : AccesoPilotoCompartido;
            // Conservar los errores, nunca devolver contraseñas en el HTML.
            input.NuevoPassword = null;
            input.ConfirmarPassword = null;
            foreach (var key in new[] { nameof(input.NuevoPassword), nameof(input.ConfirmarPassword) })
                if (ModelState.ContainsKey(key)) ModelState.SetModelValue(key, null, null);
            return View(nameof(EditarPiloto), input);
        }






        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarPiloto(int id)
        { return await CambiarBajaPilotoAsync(id); }







        // ==========================
        // ÓRDENES (Gestor)
        // ==========================
        private async Task<IActionResult> CambiarBajaPilotoAsync(int id)
        {
            var tenantId = await GetClienteAppIdDelGestorAsync();
            try
            {
                await using var tx = await _quotas.BeginTenantMutationAsync(tenantId);
                var piloto = await _db.Pilotos.SingleOrDefaultAsync(p => p.Id == id && p.ClienteAppId == tenantId);
                if (piloto == null) return NotFound();
                await _quotas.EnsureNoOpenPilotOrdersAsync(tenantId, id);
                piloto.Activo = false; piloto.EnLinea = false; piloto.Estado = EstadoPiloto.Inactivo;
                await _db.SaveChangesAsync(); await tx.CommitAsync();
                return RedirectToAction(nameof(Pilotos), new { ok = "Piloto desactivado. Se conserva su historial." });
            }
            catch (InvalidOperationException ex) { return RedirectToAction(nameof(Pilotos), new { error = ex.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> Ordenes(int? tiendaId, int? pilotoId, string? estado, int? numeroOrdenA, DateTime? fecha, string? ok = null, string? error = null, int pagina = 1, bool historial = false)
        {
            var vm = await BuildOrdersAsync(tiendaId, pilotoId, estado, numeroOrdenA, fecha, pagina, historial);
            vm.Ok = ok; vm.Error = error;
            Response.Headers.CacheControl = "no-store";
            return View(vm);
        }


        [HttpGet]
        public async Task<IActionResult> OrdenesFragmento(int? tiendaId, int? pilotoId, string? estado, int? numeroOrdenA, DateTime? fecha, int pagina = 1, bool historial = false)
        {
            Response.Headers.CacheControl = "no-store";
            return PartialView("_DespachoActualizable", await BuildOrdersAsync(tiendaId, pilotoId, estado, numeroOrdenA, fecha, pagina, historial));
        }

        private async Task<OrdenesIndexVm> BuildOrdersAsync(int? tiendaId, int? pilotoId, string? estado, int? numeroOrdenA, DateTime? fecha, int pagina, bool historial)
        {
            var tenantId = await GetClienteAppIdDelGestorAsync();
            var vm = new OrdenesIndexVm { TiendaId = tiendaId, PilotoId = pilotoId, Estado = estado, NumeroOrdenA = numeroOrdenA,
                Fecha = fecha, Pagina = Math.Clamp(pagina, 1, 1000000), EsHistorial = historial };
            vm.Tiendas = await _db.Tiendas.AsNoTracking().Where(t => t.ClienteAppId == tenantId).OrderBy(t => t.Nombre)
                .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Nombre }).ToListAsync();
            vm.Pilotos = await _db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == tenantId).OrderBy(p => p.Nombre)
                .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Nombre }).ToListAsync();
            vm.Pilotos.Insert(0, new SelectListItem { Value = "", Text = "Todos" });
            vm.Estados = Enum.GetValues<EstadoOrden>().Where(e => historial || !EstadosEntrega.Terminales.Contains(e))
                .Select(e => new SelectListItem { Value = e.ToString(), Text = e.ToString() }).ToList();
            vm.Estados.Insert(0, new SelectListItem { Value = "", Text = "Todos" });
            vm.Candidatos = await _db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == tenantId && p.Activo)
                .OrderBy(p => p.Nombre).Select(p => new PilotOperationsVm { Id = p.Id, Nombre = p.Nombre, Telefono = p.Telefono,
                    Activo = p.Activo, EnLinea = p.EnLinea, OrdenesActivas = p.Ordenes.Count(o => !EstadosEntrega.Terminales.Contains(o.Estado)) }).ToListAsync();
            var query = _db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == tenantId);
            if (!historial) query = query.Where(o => !EstadosEntrega.Terminales.Contains(o.Estado));
            if (tiendaId.HasValue) query = query.Where(o => o.TiendaId == tiendaId.Value);
            if (pilotoId.HasValue) query = query.Where(o => o.PilotoId == pilotoId.Value);
            if (!string.IsNullOrWhiteSpace(estado) && Enum.TryParse<EstadoOrden>(estado, out var parsed))
                query = query.Where(o => o.Estado == parsed);
            if (numeroOrdenA.HasValue)
            {
                var code = numeroOrdenA.Value.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                query = query.Where(o => o.CodigoA == code || (o.CodigoA == null && o.NumeroOrdenA == numeroOrdenA.Value));
            }
            if (fecha.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(fecha.Value.Date.AddHours(4), DateTimeKind.Utc);
                var toUtc = fromUtc.AddDays(1);
                query = query.Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc);
            }
            vm.Total = await query.CountAsync();
            vm.Ordenes = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
                .Skip((vm.Pagina - 1) * 50).Take(50).Select(o => new OrdenRowVm
                {
                    OrdenId = o.Id, ClienteAppId = o.ClienteAppId,
                    TiendaId = o.TiendaId, TiendaNombre = o.DatosEntregaCongelados ? o.RecogidaNombre : o.Tienda.Nombre,
                    TiendaDireccion = o.DatosEntregaCongelados ? o.RecogidaDireccion : o.Tienda.Direccion,
                    TiendaTelefono = o.DatosEntregaCongelados ? o.RecogidaTelefono : o.Tienda.Telefono,
                    NumeroOrdenA = o.NumeroOrdenA, CodigoA = o.CodigoA, Estado = o.Estado.ToString(),
                    PilotoId = o.PilotoId, PilotoNombre = o.Piloto != null ? o.Piloto.Nombre : null, PilotoTelefono = o.Piloto != null ? o.Piloto.Telefono : null,
                    CreatedAt = o.CreatedAt, Notas = o.NotaPedido, EsAuto = o.EsAuto, TiendaAutoAsignacion = o.Tienda.AutoAsignacion,
                    Precio = o.Precio, DistanciaKm = o.DistanciaKm,
                    CodigoB = o.Codigos.Where(c => c.Tipo == TipoCodigo.B_Recoleccion).Select(c => c.Codigo).FirstOrDefault()
                }).ToListAsync();
            return vm;
        }

        [HttpGet]
        public async Task<IActionResult> DetalleOrden(int id)
        {
            var tenantId = await GetClienteAppIdDelGestorAsync();
            Response.Headers.CacheControl = "no-store";
            var vm = await _db.OrdenesEntrega.AsNoTracking().Where(o => o.Id == id && o.ClienteAppId == tenantId)
                .Select(o => new OrderDetailVm { Id = o.Id, CodigoA = o.CodigoA ?? o.NumeroOrdenA.ToString(), Estado = o.Estado.ToString(),
                    Tienda = o.DatosEntregaCongelados ? o.RecogidaNombre : o.Tienda.Nombre,
                    Destinatario = o.DatosEntregaCongelados ? o.DestinoNombre : o.UsuarioFinal.Nombre,
                    Direccion = o.DatosEntregaCongelados ? o.DestinoDireccion : o.UsuarioFinal.DireccionUbicacion,
                    Piloto = o.Piloto != null ? o.Piloto.Nombre : null, Precio = o.Precio, CreatedAt = o.CreatedAt }).SingleOrDefaultAsync();
            if (vm == null) return NotFound();
            vm.Eventos = await _db.HistorialOrdenes.AsNoTracking().Where(e => e.OrdenEntregaId == id && e.OrdenEntrega.ClienteAppId == tenantId)
                .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => new OrderEventVm { CreatedAt = e.CreatedAt,
                    Tipo = e.TipoEvento.ToString(), Rol = e.ActorRol, Descripcion = e.Descripcion }).ToListAsync();
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
                .Where(c => c.Codigo == input.CodigoB && c.Tipo == TipoCodigo.B_Recoleccion
                    && c.OrdenEntrega.ClienteAppId == clienteAppId && c.OrdenEntrega.Estado == EstadoOrden.Creada
                    && (input.OrdenId == null || c.OrdenEntregaId == input.OrdenId))
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
                await _ordenService.AsignarPilotoAsync(input.CodigoB, input.PilotoId, actorId, actorRol, input.OrdenId);
                return RedirectToAction(nameof(Ordenes), new { ok = "Piloto asignado ✅" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(Ordenes), new { error = ex.Message });
            }
        }













        [HttpGet]
        public async Task<IActionResult> Estadisticas(DateTime? desde, DateTime? hasta, int? tiendaId, int? pilotoId)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (Exception ex) { return RedirectToAction(nameof(Ordenes), new { error = ex.Message }); }

            var today = DateTime.UtcNow.AddHours(-4).Date;
            var businessFrom = (desde ?? today.AddDays(-6)).Date;
            var businessTo = (hasta ?? today).Date;
            if (businessTo == DateTime.MaxValue.Date || businessTo < businessFrom || (businessTo - businessFrom).Days > 92)
                return RedirectToAction(nameof(Ordenes), new { error = "Selecciona un rango válido de hasta 93 días." });
            var from = DateTime.SpecifyKind(businessFrom.AddHours(4), DateTimeKind.Utc);
            var to = DateTime.SpecifyKind(businessTo.AddDays(1).AddHours(4), DateTimeKind.Utc);

            var vm = new EstadisticasGestorVm
            {
                Desde = businessFrom,
                Hasta = businessTo,
                TiendaId = tiendaId,
                PilotoId = pilotoId
            };

            // Dropdowns
            vm.Tiendas = await _db.Tiendas
                .AsNoTracking()
                .Where(t => t.ClienteAppId == clienteAppId)
                .OrderBy(t => t.Nombre)
                .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Nombre })
                .ToListAsync();
            vm.Tiendas.Insert(0, new SelectListItem { Value = "", Text = "Todas las Tiendas" });

            vm.Pilotos = await _db.Pilotos
                .AsNoTracking()
                .Where(p => p.ClienteAppId == clienteAppId)
                .OrderBy(p => p.Nombre)
                .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Nombre })
                .ToListAsync();
            vm.Pilotos.Insert(0, new SelectListItem { Value = "", Text = "Todos" });

            // Base query (rango + tenant)
            var q = _db.OrdenesEntrega
                .AsNoTracking()
                .Where(o => o.ClienteAppId == clienteAppId
                         && o.CreatedAt >= from
                         && o.CreatedAt < to);

            if (tiendaId.HasValue) q = q.Where(o => o.TiendaId == tiendaId.Value);
            if (pilotoId.HasValue) q = q.Where(o => o.PilotoId == pilotoId.Value);

            // =========
            // Cards: totales por estado
            // =========
            var total = await q.CountAsync();

            var entregadas = await q.CountAsync(o => o.Estado == EstadoOrden.Entregada);
            var incidentes = await q.CountAsync(o => o.Estado == EstadoOrden.Incidente);

            var pendientes = await q.CountAsync(o =>
                o.Estado == EstadoOrden.Creada ||
                o.Estado == EstadoOrden.Asignada ||
                o.Estado == EstadoOrden.Recolectada);

            vm.Cards = new EstadisticasCardsVm
            {
                Total = total,
                Entregadas = entregadas,
                Pendientes = pendientes,
                Incidentes = incidentes,
                PorcentajeEntrega = total == 0 ? 0 : (int)Math.Round((entregadas * 100.0) / total)
            };

            // =========
            // Serie: Ordenes por día
            // =========
            var rawDia = await q
                .GroupBy(o => o.CreatedAt.AddHours(-4).Date)
                .Select(g => new { Dia = g.Key, Total = g.Count(), Entregadas = g.Count(x => x.Estado == EstadoOrden.Entregada), Incidentes = g.Count(x => x.Estado == EstadoOrden.Incidente) })
                .ToListAsync();

            // Rellenar días faltantes
            var dias = Enumerable.Range(0, (to.Date - from.Date).Days)
                .Select(i => businessFrom.AddDays(i))
                .ToList();

            vm.PorDia = dias.Select(d =>
            {
                var hit = rawDia.FirstOrDefault(x => x.Dia == d);
                return new OrdenesPorDiaVm
                {
                    Dia = d,
                    Total = hit?.Total ?? 0,
                    Entregadas = hit?.Entregadas ?? 0,
                    Incidentes = hit?.Incidentes ?? 0
                };
            }).ToList();

            // =========
            // Rankings: Pilotos y Tiendas
            // =========
            vm.TopPilotos =
      await (from o in q
             where o.PilotoId != null
             join p in _db.Pilotos.AsNoTracking() on o.PilotoId equals p.Id
             group o by new { p.Id, p.Nombre } into g
             select new RankingPilotoVm
             {
                 PilotoId = g.Key.Id,
                 Nombre = g.Key.Nombre,
                 Total = g.Count(),
                 Entregadas = g.Count(x => x.Estado == EstadoOrden.Entregada),
                 Incidentes = g.Count(x => x.Estado == EstadoOrden.Incidente),

                 // ✅ Average traducible: int? -> Average() => double? -> ?? 0
                 PromedioMinutosEntrega = (int)Math.Round(
                     g.Where(x => x.EntregadaAt != null)
                      .Select(x => (int?)EF.Functions.DateDiffMinute(x.CreatedAt, x.EntregadaAt!.Value))
                      .Average() ?? 0
                 )
             })
            .OrderByDescending(x => x.Entregadas)
            .ThenBy(x => x.Incidentes)
            .Take(8)
            .ToListAsync();


            vm.TopTiendas =
       await (from o in q
              join t in _db.Tiendas.AsNoTracking() on o.TiendaId equals t.Id
              group o by new { t.Id, t.Nombre } into g
              select new RankingTiendaVm
              {
                  TiendaId = g.Key.Id,
                  Nombre = g.Key.Nombre,
                  Total = g.Count(),
                  Entregadas = g.Count(x => x.Estado == EstadoOrden.Entregada),
                  Incidentes = g.Count(x => x.Estado == EstadoOrden.Incidente)
              })
             .OrderByDescending(x => x.Total)
             .Take(8)
             .ToListAsync();


            // =========
            // Tiempos (promedios)
            // =========
            // Created -> Assigned
            // =========
            // Tiempos (promedios) - versión traducible a SQL
            // =========
            vm.Tiempos = new TiemposProcesoVm
            {
                MinCreadaAsignada =
                    await q.Where(o => o.AssignedAt != null)
                        .Select(o => (int?)EF.Functions.DateDiffMinute(o.CreatedAt, o.AssignedAt!.Value))
                        .AverageAsync() ?? 0,

                MinAsignadaRecolectada =
                    await q.Where(o => o.AssignedAt != null && o.RecolectadaAt != null)
                        .Select(o => (int?)EF.Functions.DateDiffMinute(o.AssignedAt!.Value, o.RecolectadaAt!.Value))
                        .AverageAsync() ?? 0,

                MinRecolectadaEntregada =
                    await q.Where(o => o.RecolectadaAt != null && o.EntregadaAt != null)
                        .Select(o => (int?)EF.Functions.DateDiffMinute(o.RecolectadaAt!.Value, o.EntregadaAt!.Value))
                        .AverageAsync() ?? 0,

                MinTotalCreadaEntregada =
                    await q.Where(o => o.EntregadaAt != null)
                        .Select(o => (int?)EF.Functions.DateDiffMinute(o.CreatedAt, o.EntregadaAt!.Value))
                        .AverageAsync() ?? 0
            };

            vm.Tiempos.RoundAll();


            // redondeo final
            vm.Tiempos.RoundAll();

            return View(vm);
        }

        // ==========================
        // SEGUIMIENTO GPS (mapa + precios)
        // ==========================

        [HttpGet]
        public async Task<IActionResult> Mapa(string? ok = null, string? error = null)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Ordenes), new { error = ex.Message }); }

            var cliente = await _db.ClientesApp.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == clienteAppId);

            var vm = new MapaPilotosVm
            {
                PrecioBase = cliente?.PrecioBase ?? 0m,
                PrecioPorKm = cliente?.PrecioPorKm ?? 0m,
                Ok = ok,
                Error = error,
                Pilotos = await ObtenerPilotosMapaAsync(clienteAppId)
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> MapaData()
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception) { return Json(new List<PilotoMapaVm>()); }

            var pilotos = await ObtenerPilotosMapaAsync(clienteAppId);
            return Json(pilotos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarPrecios(decimal precioBase, decimal precioPorKm)
        {
            int clienteAppId;
            try { clienteAppId = await GetClienteAppIdDelGestorAsync(); }
            catch (System.Exception ex) { return RedirectToAction(nameof(Mapa), new { error = ex.Message }); }

            if (precioBase < 0 || precioPorKm < 0)
                return RedirectToAction(nameof(Mapa), new { error = "Los precios no pueden ser negativos." });

            var cliente = await _db.ClientesApp.SingleOrDefaultAsync(c => c.Id == clienteAppId);
            if (cliente is null)
                return RedirectToAction(nameof(Mapa), new { error = "ClienteApp no encontrado." });

            cliente.PrecioBase = precioBase;
            cliente.PrecioPorKm = precioPorKm;
            cliente.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Mapa), new { ok = "Tarifas guardadas ✅" });
        }

        private async Task<List<PilotoMapaVm>> ObtenerPilotosMapaAsync(int clienteAppId)
        {
            return await _db.Pilotos.AsNoTracking()
                .Where(p => p.ClienteAppId == clienteAppId)
                .OrderBy(p => p.Nombre)
                .Select(p => new PilotoMapaVm
                {
                    Id = p.Id,
                    Nombre = p.Nombre,
                    Estado = !p.Activo ? "Inactivo" : p.Ordenes.Any(o => !EstadosEntrega.Terminales.Contains(o.Estado)) ? "Con entregas" : "Sin entregas",
                    EstadoNum = !p.Activo ? 3 : p.Ordenes.Any(o => !EstadosEntrega.Terminales.Contains(o.Estado)) ? 2 : 1,
                    Activo = p.Activo, EnLinea = p.EnLinea, OrdenesActivas = p.Ordenes.Count(o => !EstadosEntrega.Terminales.Contains(o.Estado)),
                    Latitud = p.Latitud,
                    Longitud = p.Longitud,
                    UltimaUbicacionAt = p.UltimaUbicacionAt
                })
                .ToListAsync();
        }
    }
}
