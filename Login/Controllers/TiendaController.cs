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
        private readonly ILogger<TiendaController> _logger;

        public TiendaController(ApplicationDbContext db, IOrdenService ordenService, IUserContextService userCtx, ILogger<TiendaController> logger)
        {
            _db = db;
            _ordenService = ordenService;
            _userCtx = userCtx;
            _logger = logger;
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

            var coords = Login.Application.Services.MapaLinkParser.Parse(input.MapaLink);

            var uf = new UsuarioFinal
            {
                TiendaId = tienda.Id,
                Nombre = input.Nombre.Trim(),
                DireccionUbicacion = input.DireccionUbicacion.Trim(),
                Telefono = input.Telefono.Trim(),
                MapaLink = string.IsNullOrWhiteSpace(input.MapaLink) ? null : input.MapaLink.Trim(),
                Latitud = coords?.Lat,
                Longitud = coords?.Lng,
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

            var coordsEdit = Login.Application.Services.MapaLinkParser.Parse(input.MapaLink);
            u.Latitud = coordsEdit?.Lat;
            u.Longitud = coordsEdit?.Lng;

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
            var vm = new CrearOrdenVm { OperacionId = Guid.NewGuid().ToString("N"), Ok = ok, Error = error };
            await PrepararFormularioOrdenAsync(vm, tienda);
            return View(vm);
        }

        private async Task PrepararFormularioOrdenAsync(CrearOrdenVm vm, Tienda tienda)
        {
            vm.ClienteAppId = tienda.ClienteAppId;
            vm.TiendaId = tienda.Id;
            vm.UsuariosFinales = await _db.UsuariosFinales.AsNoTracking()
                .Where(u => u.TiendaId == tienda.Id && u.Activo).OrderBy(u => u.Nombre)
                .Select(u => new SelectListItem { Value = u.Id.ToString(), Text = u.Nombre }).ToListAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearOrdenPost(CrearOrdenVm input)
        {
            // Otra sesión puede cambiar la preferencia sin mover esta operación.
            Tienda? tienda;
            if (input.TiendaId > 0)
            {
                if (!await _userCtx.UsuarioTieneAccesoATiendaAsync(GetUserId(), input.TiendaId)) return Forbid();
                tienda = await _db.Tiendas.AsNoTracking().SingleOrDefaultAsync(t => t.Id == input.TiendaId && t.Activo);
            }
            else
            {
                try { tienda = await GetTiendaActivaAsync(); }
                catch { return RedirectToAction(nameof(SeleccionarTienda)); }
            }
            if (tienda is null) return Forbid();
            if (!ModelState.IsValid)
            {
                input.Error = "Revisa los datos de la orden.";
                await PrepararFormularioOrdenAsync(input, tienda);
                return View("CrearOrden", input);
            }
            try
            {
                var res = await _ordenService.CrearOrdenAsync(tienda.ClienteAppId, tienda.Id, input.UsuarioFinalId,
                    input.NotaPedido, GetUserId(), "Cliente", input.OperacionId);
                return RedirectToAction(nameof(Comprobante), new { ordenId = res.OrdenId, tiendaId = tienda.Id });
            }
            catch (InvalidOperationException ex) { input.Error = ex.Message; }
            catch (ArgumentException ex) { input.Error = ex.Message; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo confirmar la creación web. TraceId {TraceId}", HttpContext.TraceIdentifier);
                input.Error = "No se pudo confirmar la creación. Reintenta con este mismo formulario para recuperar el resultado.";
            }
            await PrepararFormularioOrdenAsync(input, tienda);
            return View("CrearOrden", input);
        }

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Comprobante(int ordenId)
        {
            var vm = await ConstruirComprobanteAsync(ordenId);
            return vm is null ? NotFound() : View(vm);
        }

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> QrCodigos(int ordenId)
        {
            var vm = await ConstruirComprobanteAsync(ordenId);
            return vm is null ? NotFound() : PartialView("_ModalQrCodigos", vm);
        }

        private async Task<QrCodigosVm?> ConstruirComprobanteAsync(int ordenId)
        {
            Tienda tienda;
            try { tienda = await GetTiendaActivaAsync(); }
            catch { return null; }
            var orden = await _db.OrdenesEntrega.AsNoTracking().Include(o => o.UsuarioFinal).Include(o => o.Codigos)
                .SingleOrDefaultAsync(o => o.Id == ordenId && o.TiendaId == tienda.Id && o.ClienteAppId == tienda.ClienteAppId);
            if (orden is null) return null;
            return new QrCodigosVm
            {
                OrdenId = orden.Id, TiendaId = tienda.Id, NumeroOrdenA = orden.NumeroOrdenA, CodigoA = orden.CodigoA,
                Estado = orden.Estado.ToString(),
                CodigoB = orden.Codigos.Single(c => c.Tipo == TipoCodigo.B_Recoleccion).Codigo,
                CodigoC = orden.Codigos.Single(c => c.Tipo == TipoCodigo.C_Finalizacion).Codigo,
                UsuarioFinalNombre = orden.DatosEntregaCongelados ? orden.DestinoNombre : orden.UsuarioFinal.Nombre,
                DireccionUbicacion = orden.DatosEntregaCongelados ? orden.DestinoDireccion : orden.UsuarioFinal.DireccionUbicacion,
                MapaLink = orden.DatosEntregaCongelados ? orden.DestinoMapaLink : orden.UsuarioFinal.MapaLink,
                Descripcion = orden.NotaPedido
            };
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
                    CodigoA = o.CodigoA,
                        Estado = o.Estado.ToString(),
                        EstadoNum = (int)o.Estado,
                        UsuarioFinal = o.DatosEntregaCongelados ? o.DestinoNombre : o.UsuarioFinal.Nombre,
                        CreatedAt = o.CreatedAt,
                        AssignedAt = o.AssignedAt,
                        RecolectadaAt = o.RecolectadaAt,
                        EntregadaAt = o.EntregadaAt,
                        Precio = o.Precio,
                        DistanciaKm = o.DistanciaKm
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
