using System.Security.Claims;
using Login.Data;
using Login.Application.Interfaces;
using Login.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Login.Filters
{
    public class ClienteAppEstadoFilter : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _db;
        private readonly IUserContextService _userContext;

        public ClienteAppEstadoFilter(ApplicationDbContext db, IUserContextService userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;

            // 0) Si no está autenticado, no bloquees
            if (user?.Identity?.IsAuthenticated != true)
            {
                await next();
                return;
            }

            // 1) Nunca bloquear SuperAdmin
            if (user.IsInRole("SuperAdmin"))
            {
                await next();
                return;
            }

            // 2) Evitar loop: permitir la pantalla de bloqueo
            var routeController = context.RouteData.Values["controller"]?.ToString() ?? "";
            var routeAction = context.RouteData.Values["action"]?.ToString() ?? "";
            if (routeController.Equals("Bloqueo", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // 3) Permitir Logout (para que pueda salir sí o sí)
            // Nota: si tu logout está en Identity, lo cubrimos por path también
            var path = context.HttpContext.Request.Path.Value?.ToLower() ?? "";
            // La selección valida el tenant de destino en UserContextService. Debe
            // seguir accesible si la tienda seleccionada fue suspendida o revocada.
            if (routeController.Equals("Auth", StringComparison.OrdinalIgnoreCase)
                || path == "/api/tienda/tiendas" || path == "/api/tienda/tienda-activa"
                || (routeController == "Tienda" && routeAction == "SeleccionarTienda"))
            {
                await next();
                return;
            }
            if (path.Contains("/logout") || path.Contains("/identity/account/logout"))
            {
                await next();
                return;
            }

            // 4) Solo aplicar a Gestor/Cliente/Piloto (tu opción C)
            var aplica =
                user.IsInRole("Gestor") ||
                user.IsInRole("Cliente") ||
                user.IsInRole("Piloto");

            if (!aplica)
            {
                await next();
                return;
            }

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                await next();
                return;
            }

            // 5) Resolver ClienteAppId según rol
            int clienteAppId = 0;

            if (path.StartsWith("/api/piloto") || routeController == "Piloto")
            {
                clienteAppId = await _db.Pilotos.AsNoTracking()
                    .Where(p => p.IdentityUserId == userId && p.Activo)
                    .Select(p => p.ClienteAppId).SingleOrDefaultAsync();
            }
            else if (user.IsInRole("Gestor") && !path.StartsWith("/api/tienda") && routeController != "Tienda")
            {
                clienteAppId = await _db.ClientesApp.AsNoTracking()
                    .Where(c => c.GestorIdentityUserId == userId)
                    .Select(c => c.Id)
                    .SingleOrDefaultAsync();
            }
            else if (user.IsInRole("Cliente"))
            {
                try
                {
                    var tiendaId = await _userContext.GetTiendaActivaIdAsync(userId);
                    clienteAppId = await _db.Tiendas.AsNoTracking().Where(t => t.Id == tiendaId)
                        .Select(t => t.ClienteAppId).SingleOrDefaultAsync();
                }
                catch (InvalidOperationException)
                {
                    Bloquear(context, "Tienda no disponible");
                    return;
                }
            }
            else if (user.IsInRole("Piloto"))
            {
                clienteAppId = await _db.Pilotos.AsNoTracking()
                    .Where(p => p.IdentityUserId == userId && p.Activo)
                    .Select(p => p.ClienteAppId)
                    .SingleOrDefaultAsync();
            }

            if (clienteAppId == 0)
            {
                Bloquear(context, "Usuario sin tenant activo");
                return;
            }

            // 6) Leer estado del tenant
            var estado = await _db.ClientesApp.AsNoTracking()
                .Where(c => c.Id == clienteAppId)
                .Select(c => c.Estado)
                .SingleOrDefaultAsync();

            if (estado != EstadoClienteApp.Activo)
            {
                // Rutas API -> devolver 403 JSON (los móviles no entienden redirects)
                var apiPath = context.HttpContext.Request.Path.Value?.ToLower() ?? "";
                if (apiPath.StartsWith("/api/"))
                {
                    context.Result = new JsonResult(new
                    {
                        error = "Tenant no activo",
                        motivo = estado.ToString()
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }

                context.Result = new RedirectToActionResult("Index", "Bloqueo", new { motivo = estado.ToString() });
                return;
            }

            await next();
        }

        private static void Bloquear(ActionExecutingContext context, string motivo)
        {
            context.Result = context.HttpContext.Request.Path.StartsWithSegments("/api")
                ? new JsonResult(new { error = motivo }) { StatusCode = StatusCodes.Status403Forbidden }
                : new RedirectToActionResult("Index", "Bloqueo", new { motivo });
        }
    }
}
