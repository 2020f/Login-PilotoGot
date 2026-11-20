using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Envirapido.Portal.Contracts;
using Login.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Login.Application.Portal;

public static class PortalCatalogEndpoint
{
    // Endpoint mínimo: no pasa por el filtro de empresa del MVC ni usa cookies
    // de una empresa. Solo autoriza la clave de lectura entre servidores.
    public static IEndpointConventionBuilder MapPortalCatalog(this WebApplication app)
        => app.MapGet("/api/internal/portal/catalog", async (HttpContext context, ApplicationDbContext db,
            IOptions<PortalIntegrationOptions> configured, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var options = configured.Value;
            var supplied = context.Request.Headers["X-Envirapido-Catalog-Key"].ToString();
            if (!options.Enabled || !PortalIntegrationOptions.StrongKey(options.CatalogReadKey)
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
                    SHA256.HashData(Encoding.UTF8.GetBytes(options.CatalogReadKey))))
                return Results.Json(new { error = "Acceso de catálogo no autorizado." }, statusCode: 401);
            var currency = options.CatalogCurrency.ToUpperInvariant();
            if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
                return Results.Json(new { error = "La moneda del catálogo no está configurada." }, statusCode: 503);
            var source = await db.Planes.AsNoTracking().Where(p => p.Activo).OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.Nombre, p.PrecioMensual, p.MaxTiendas, p.MaxPilotos, p.MaxOrdenesPorMes }).ToListAsync(ct);
            var plans = source.Select(p => new CrmPortalPlan($"plan-{p.Id}", p.Nombre, p.PrecioMensual,
                currency, p.MaxTiendas, p.MaxPilotos, p.MaxOrdenesPorMes)).ToArray();
            var version = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plans)));
            context.Response.Headers.ETag = $"\"{version}\"";
            return Results.Json(new CrmPortalCatalog(plans, version));
        }).AllowAnonymous();
}
