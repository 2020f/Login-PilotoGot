using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.ViewModels.Tienda;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;


namespace Login.Controllers
{
    [Authorize(Roles = "Cliente")] // ✅ Interfaz Tienda
    public class TiendaController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IOrdenService _ordenService;
        private readonly IUserContextService _userCtx;

        public TiendaController(ApplicationDbContext db, IOrdenService ordenService, IUserContextService userCtx)
        {
            _db = db;
            _ordenService = ordenService;
            _userCtx = userCtx;
        }

        private string GetUserId()
            => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        private async Task<Tienda> GetTiendaActivaAsync()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
                throw new System.InvalidOperationException("Usuario no autenticado.");

            var tiendaId = await _userCtx.GetTiendaActivaIdAsync(userId);

            // Seguridad: validar que realmente tiene acceso a esa tienda
            var tieneAcceso = await _userCtx.UsuarioTieneAccesoATiendaAsync(userId, tiendaId);
            if (!tieneAcceso)
                throw new System.InvalidOperationException("No tienes acceso a la tienda activa.");

            var tienda = await _db.Tiendas.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == tiendaId && t.Activo);

            if (tienda is null)
                throw new System.InvalidOperationException("Tienda activa inválida o desactivada.");

            return tienda;
        }

        // ==========================
        // 0) Esta vista solo la utilizo para mandar un mensaje 
        // ==========================
        [HttpGet]
        public async Task<IActionResult> SeleccionarTienda(string? ok = null, string? error = null)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Redirect("/Identity/Account/Login");

            var tiendas = await _db.UsuariosTienda
                .AsNoTracking()
                .Include(x => x.Tienda)
                .Where(x => x.IdentityUserId == userId && x.Activo && x.Tienda.Activo)
                .OrderBy(x => x.Tienda.Nombre)
                .Select(x => new SelectListItem
                {
                    Value = x.TiendaId.ToString(),
                    Text = x.Tienda.Nombre
                })
                .ToListAsync();

            var vm = new SeleccionarTiendaVm
            {
                Ok = ok,
                Error = error,
                Tiendas = tiendas
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeleccionarTienda(int tiendaId)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Redirect("/Identity/Account/Login");

            try
            {
                await _userCtx.SetTiendaActivaIdAsync(userId, tiendaId);
                return RedirectToAction(nameof(Ordenes), new { ok = "Tienda activa actualizada ✅" });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(SeleccionarTienda), new { error = ex.Message });
            }
        }

        // ==========================
        // 1) USUARIOS FINALES (list + create)
        // ==========================

        [HttpGet]
        public async Task<IActionResult> UsuariosFinales(string? ok = null, string? error = null)
        {
            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            var vm = new UsuariosFinalesIndexVm
            {
                TiendaId = tienda.Id, // lo dejamos para links internos
                Ok = ok,
                Error = error,
                Usuarios = await _db.UsuariosFinales.AsNoTracking()
                    .Where(u => u.TiendaId == tienda.Id)
                    .OrderByDescending(u => u.CreatedAt)
                    .Select(u => new UsuarioFinalRowVm
                    {
                        Id = u.Id,
                        Nombre = u.Nombre,
                        DireccionUbicacion = u.DireccionUbicacion,
                        MapaLink = u.MapaLink,
                        Telefono = u.Telefono,
                        Activo = u.Activo,
                        CreatedAt = u.CreatedAt
                    })
                    .ToListAsync()
            };

            return View(vm);
        }

        [HttpGet]
        public IActionResult CrearUsuarioFinal()
        {
            // ya no necesitamos tiendaId aquí
            return View(new UsuarioFinalCreateVm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearUsuarioFinal(UsuarioFinalCreateVm input)
        {
            if (!ModelState.IsValid) return View(input);

            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            var uf = new UsuarioFinal
            {
                TiendaId = tienda.Id,
                Nombre = input.Nombre.Trim(),
                DireccionUbicacion = input.DireccionUbicacion.Trim(),
                Telefono = input.Telefono.Trim(),
                MapaLink = string.IsNullOrWhiteSpace(input.MapaLink) ? null : input.MapaLink.Trim(),
                Notas = string.IsNullOrWhiteSpace(input.Notas) ? null : input.Notas.Trim(),
                Activo = true
            };

            _db.UsuariosFinales.Add(uf);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(UsuariosFinales), new { ok = "Usuario final creado ✅" });
        }


        // ==========================
        // 1.1) EDITAR USUARIO FINAL
        // ==========================

        [HttpGet]
        public async Task<IActionResult> EditarUsuarioFinal(int id, string? ok = null, string? error = null)
        {
            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            ViewBag.Ok = ok;
            ViewBag.Error = error;

            var u = await _db.UsuariosFinales
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.TiendaId == tienda.Id);

            if (u is null)
                return RedirectToAction(nameof(UsuariosFinales), new { error = "Usuario final no encontrado o no es de tu tienda." });

            var vm = new UsuarioFinalEditVm
            {
                Id = u.Id,
                Nombre = u.Nombre,
                DireccionUbicacion = u.DireccionUbicacion,
                MapaLink = u.MapaLink,
                Telefono = u.Telefono,
                Notas = u.Notas,
                Activo = u.Activo
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarUsuarioFinal(UsuarioFinalEditVm input)
        {
            if (!ModelState.IsValid) return View(input);

            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            var u = await _db.UsuariosFinales
                .SingleOrDefaultAsync(x => x.Id == input.Id && x.TiendaId == tienda.Id);

            if (u is null)
                return RedirectToAction(nameof(UsuariosFinales), new { error = "Usuario final no encontrado o no es de tu tienda." });

            u.Nombre = input.Nombre.Trim();
            u.DireccionUbicacion = input.DireccionUbicacion.Trim();
            u.Telefono = input.Telefono.Trim();
            u.MapaLink = string.IsNullOrWhiteSpace(input.MapaLink) ? null : input.MapaLink.Trim();
            u.Notas = string.IsNullOrWhiteSpace(input.Notas) ? null : input.Notas.Trim();
            u.Activo = input.Activo;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(UsuariosFinales), new { ok = "Usuario final actualizado ✅" });
        }

        // ==========================
        // 1.2) ELIMINAR USUARIO FINAL
        // ==========================

        [HttpGet]
        public async Task<IActionResult> EliminarUsuarioFinal(int id)
        {
            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            var u = await _db.UsuariosFinales
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.TiendaId == tienda.Id);

            if (u is null)
                return RedirectToAction(nameof(UsuariosFinales), new { error = "Usuario final no encontrado o no es de tu tienda." });

            // ✅ Regla: no se puede borrar si tiene órdenes
            var tieneOrdenes = await _db.OrdenesEntrega
                .AsNoTracking()
                .AnyAsync(o => o.UsuarioFinalId == u.Id);

            if (tieneOrdenes)
                return RedirectToAction(nameof(UsuariosFinales), new { error = "No puedes eliminar: este usuario final ya tiene órdenes." });

            var vm = new UsuarioFinalDeleteVm
            {
                Id = u.Id,
                Nombre = u.Nombre,
                DireccionUbicacion = u.DireccionUbicacion,
                Telefono = u.Telefono
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarUsuarioFinalConfirmado(int id)
        {
            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            var u = await _db.UsuariosFinales
                .SingleOrDefaultAsync(x => x.Id == id && x.TiendaId == tienda.Id);

            if (u is null)
                return RedirectToAction(nameof(UsuariosFinales), new { error = "Usuario final no encontrado o no es de tu tienda." });

            var tieneOrdenes = await _db.OrdenesEntrega
                .AsNoTracking()
                .AnyAsync(o => o.UsuarioFinalId == u.Id);

            if (tieneOrdenes)
                return RedirectToAction(nameof(UsuariosFinales), new { error = "No puedes eliminar: este usuario final ya tiene órdenes." });

            _db.UsuariosFinales.Remove(u);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(UsuariosFinales), new { ok = "Usuario final eliminado ✅" });
        }






        // ==========================
        // 2) CREAR ORDEN
        // ==========================








        [HttpGet]
        public async Task<IActionResult> CrearOrden(string? ok = null, string? error = null)
        {
            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            var usuarios = await _db.UsuariosFinales.AsNoTracking()
                .Where(u => u.TiendaId == tienda.Id && u.Activo)
                .OrderBy(u => u.Nombre)
                .Select(u => new SelectListItem { Value = u.Id.ToString(), Text = u.Nombre })
                .ToListAsync();

            var vm = new CrearOrdenVm
            {
                ClienteAppId = tienda.ClienteAppId,
                TiendaId = tienda.Id,
                UsuariosFinales = usuarios,
                Ok = ok,
                Error = error
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearOrdenPost(CrearOrdenVm input)
        {
            // input trae UsuarioFinalId + NotaPedido (y tiene ClienteAppId/TiendaId hidden)
            if (!ModelState.IsValid)
                return RedirectToAction(nameof(CrearOrden), new { error = "Datos inválidos." });

            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            var actorId = GetUserId();
            var actorRol = "Cliente";

            try
            {
                var res = await _ordenService.CrearOrdenAsync(
                    clienteAppId: tienda.ClienteAppId,
                    tiendaId: tienda.Id,
                    usuarioFinalId: input.UsuarioFinalId,
                    notaPedido: input.NotaPedido,
                    actorIdentityUserId: actorId,
                    actorRol: actorRol);

                return RedirectToAction(nameof(CrearOrden), new
                {
                    ok = $"Orden creada ✅ A={res.NumeroOrdenA} | B={res.CodigoB} | C={res.CodigoC}"
                });
            }
            catch (System.Exception ex)
            {
                return RedirectToAction(nameof(CrearOrden), new { error = ex.Message });
            }
        }



        

[HttpGet]
    public async Task<IActionResult> QrCodigos(int ordenId)
    {
        Tienda tienda;
        try { tienda = await GetTiendaActivaAsync(); }
        catch { return Unauthorized(); }

        var orden = await _db.OrdenesEntrega
            .AsNoTracking()
            .Include(o => o.UsuarioFinal)
            .Include(o => o.Codigos)
            .FirstOrDefaultAsync(o => o.Id == ordenId && o.TiendaId == tienda.Id);

        if (orden == null) return NotFound();

        // 🔵 Código B (para piloto / recolección)
        var codigoB = orden.Codigos
            .Where(x => x.Tipo == TipoCodigo.B_Recoleccion)
            .Select(x => x.Codigo)
            .FirstOrDefault();

        // 🟢 Código C (para usuario final / entrega)
        var codigoC = orden.Codigos
            .Where(x => x.Tipo == TipoCodigo.C_Finalizacion)
            .Select(x => x.Codigo)
            .FirstOrDefault();

        var vm = new Login.ViewModels.Tienda.QrCodigosVm
        {
            OrdenId = orden.Id,
            NumeroOrdenA = orden.NumeroOrdenA,
            Estado = orden.Estado.ToString(),

            CodigoB = codigoB ?? "",
            CodigoC = codigoC ?? "",

            UsuarioFinalNombre = orden.UsuarioFinal.Nombre,
            DireccionUbicacion = orden.UsuarioFinal.DireccionUbicacion,
            MapaLink = orden.UsuarioFinal.MapaLink,
            Descripcion = orden.NotaPedido
        };

        return PartialView("_ModalQrCodigos", vm);
    }









    // ==========================
    // 3) ORDENES DE LA TIENDA
    // ==========================

    [HttpGet]
        public async Task<IActionResult> Ordenes(string? ok = null, string? error = null)
        {
            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return RedirectToAction(nameof(SeleccionarTienda)); }

            var vm = new OrdenesTiendaVm
            {
                TiendaId = tienda.Id,
                Ordenes = await _db.OrdenesEntrega.AsNoTracking()
                    .Include(o => o.UsuarioFinal)
                    .Where(o => o.TiendaId == tienda.Id)
                    .OrderByDescending(o => o.CreatedAt)
                    .Take(200)
                    .Select(o => new OrdenTiendaRowVm
                    {
                        OrdenId = o.Id,
                        NumeroOrdenA = o.NumeroOrdenA,
                        Estado = o.Estado.ToString(),
                        EstadoNum = (int)o.Estado,
                        UsuarioFinal = o.UsuarioFinal.Nombre,
                        CreatedAt = o.CreatedAt,
                        AssignedAt = o.AssignedAt,
                        RecolectadaAt = o.RecolectadaAt,
                        EntregadaAt = o.EntregadaAt
                    })
                    .ToListAsync()
            };

            // mensajes por querystring
            // (si tu vm no tiene Ok/Error, no pasa nada, puedes ignorarlos)
            ViewBag.Ok = ok;
            ViewBag.Error = error;

            return View(vm);
        }
    }
}
