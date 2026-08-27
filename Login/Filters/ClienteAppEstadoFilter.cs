using System.Security.Claims;
using Login.Data;
using Login.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Login.Filters
{
    public class ClienteAppEstadoFilter : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _db;

        public ClienteAppEstadoFilter(ApplicationDbContext db)
        {
            _db = db;
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

            if (user.IsInRole("Gestor"))
            {
                clienteAppId = await _db.ClientesApp.AsNoTracking()
                    .Where(c => c.GestorIdentityUserId == userId)
                    .Select(c => c.Id)
                    .SingleOrDefaultAsync();
            }
            else if (user.IsInRole("Cliente"))
            {
                clienteAppId = await _db.UsuariosTienda.AsNoTracking()
                    .Where(ut => ut.IdentityUserId == userId && ut.Activo)
                    .Select(ut => ut.Tienda.ClienteAppId)
                    .SingleOrDefaultAsync();
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
                await next();
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
    }
}
