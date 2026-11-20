using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Envirapido.Portal.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Envirapido.Portal.Api.Security;

public static class PortalSecurity
{
    public const string CookieScheme = "EnvirapidoPortal";
    public const string AdminScheme = "EnvirapidoPortalAdmin";
    public const string AdminPolicy = "PortalAdmin";
    public static Guid UserId(HttpContext context) => Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        && context.User.Identity?.AuthenticationType == CookieScheme ? id : throw new PortalException(401, "SESSION_REQUIRED", "Inicia sesión en el portal.");
    public static string Actor(HttpContext context) => context.User.FindFirstValue("portal-operator")
        ?? throw new PortalException(401, "ADMIN_REQUIRED", "Acceso administrativo no autorizado.");
    public static Task ValidateCsrfAsync(HttpContext context) => context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool SameSecret(string expected, string actual) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
    public static string OperationKey(HttpContext c)
    {
        if (c.Request.Headers["Idempotency-Key"].Count != 1 || !Guid.TryParse(c.Request.Headers["Idempotency-Key"], out var key))
            throw new PortalException(400, "OPERATION_KEY_REQUIRED", "Envía Idempotency-Key con un UUID estable para esta operación.");
        return key.ToString("N");
    }
}
public sealed class PortalException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed class PortalAdminAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, PortalOptions portal)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var keys = Request.Headers["X-Envirapido-Portal-Key"];
        var actors = Request.Headers["X-Envirapido-Operator"];
        if (keys.Count != 1 || actors.Count != 1 || keys[0] is not { Length: >= 32 and <= 512 } key
            || actors[0] is not { Length: > 0 and <= 150 } actor || actor.Any(char.IsControl) || actor.Trim() != actor
            || !PortalSecurity.SameSecret(portal.AdminKey, key))
            return Task.FromResult(AuthenticateResult.Fail("Acceso administrativo no autorizado."));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("portal-operator", actor)], PortalSecurity.AdminScheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, PortalSecurity.AdminScheme)));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    { Response.StatusCode = 401; return Response.WriteAsJsonAsync(new { error = "Acceso administrativo no autorizado.", code = "ADMIN_REQUIRED" }); }
}
public sealed class PortalCookieEvents(PortalDbContext db, TimeProvider clock) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var uid = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var sid = context.Principal?.FindFirstValue("portal-session");
        if (!Guid.TryParse(uid, out var userId) || !Guid.TryParse(sid, out var sessionId)) { context.RejectPrincipal(); return; }
        var session = await db.Sessions.AsNoTracking().Include(s => s.User).SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, context.HttpContext.RequestAborted);
        if (session is null || session.RevokedAt != null || session.ExpiresAt <= clock.GetUtcNow() || session.Stamp != session.User.SecurityStamp || session.User.Status != "Active")
            context.RejectPrincipal();
    }
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    { context.Response.StatusCode = 401; return context.Response.WriteAsJsonAsync(new { error = "Inicia sesión en el portal.", code = "SESSION_REQUIRED" }); }
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    { context.Response.StatusCode = 403; return context.Response.WriteAsJsonAsync(new { error = "No tienes autorización para esta operación.", code = "ACCESS_DENIED" }); }
}
