using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Security;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Envirapido.Portal.Api.Services;

public sealed partial class PortalDomainService
{
    public async Task<Guid> RegisterAsync(PortalRegisterInput input, string key, CancellationToken ct)
    {
        var email = Email(input.Email); Password(input.Password, input.ConfirmPassword); var profile = CheckProfile(input.Profile);
        if (input.TermsVersion != options.TermsVersion || input.PrivacyVersion != options.PrivacyVersion)
            throw Error(409, "POLICIES_CHANGED", "Revisa y acepta las políticas vigentes.");
        var userId = await IdempotentAsync("register:" + email, key, new { email, profile, input.PlanCode, input.PlanVersion, input.TermsVersion, input.PrivacyVersion }, async () =>
        {
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == email, ct)) throw Error(409, "ACCOUNT_CONFLICT", "No se pudo registrar esta cuenta. Comprueba los datos o recupera tu acceso.");
            var plan = await catalog.ForPurchaseAsync(input.PlanCode, input.PlanVersion, ct);
            var user = new PortalUser { Email = email, NormalizedEmail = email, CreatedAt = Now, TermsVersion = options.TermsVersion, PrivacyVersion = options.PrivacyVersion };
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            var p = new PortalProfile { UserId = user.Id }; SetProfile(p, profile);
            db.Users.Add(user); db.Profiles.Add(p);
            var request = new PortalPurchaseRequest { UserId = user.Id, PlanSnapshotJson = JsonSerializer.Serialize(plan, Json), ProfileSnapshotJson = JsonSerializer.Serialize(profile, Json), Amount = plan.MonthlyPrice, Currency = plan.Currency, CreatedAt = Now };
            db.Requests.Add(request);
            await QueueTokenAsync(user, "ConfirmEmail", ct);
            audit.Add(user.Id.ToString(), "AccountRegistered", "PortalUser", user.Id.ToString(), "Cuenta comercial y solicitud registradas; sin acceso ni usuario CRM.");
            audit.Add(user.Id.ToString(), "RequestSubmitted", "PortalRequest", request.Id.ToString(), $"Solicitud de {plan.Code}; {plan.Currency} {plan.MonthlyPrice:0.00}.");
            return user.Id;
        }, ct);
        var registered = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (hasher.VerifyHashedPassword(registered, registered.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
            throw Error(409, "ACCOUNT_CONFLICT", "No se pudo completar esta operación de registro.");
        return userId;
    }
    public async Task<PortalUser> LoginAsync(PortalLoginInput input, CancellationToken ct)
    {
        var email = Email(input.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == email, ct);
        if (input.Password is null || input.Password.Length > 100 || user is null || user.LockedUntil > Now)
            throw Error(401, "INVALID_CREDENTIALS", "Credenciales inválidas o acceso temporalmente bloqueado.");
        var verified = hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password);
        if (verified == PasswordVerificationResult.Failed)
        {
            user.FailedLogins++; if (user.FailedLogins >= 5) { user.LockedUntil = Now.AddMinutes(15); user.FailedLogins = 0; }
            user.Version = Guid.NewGuid().ToString("N"); await db.SaveChangesAsync(ct);
            throw Error(401, "INVALID_CREDENTIALS", "Credenciales inválidas o acceso temporalmente bloqueado.");
        }
        if (user.Status != "Active") throw Error(403, "ACCOUNT_SUSPENDED", "Esta cuenta del portal está suspendida. Contacta a administración.");
        user.FailedLogins = 0; user.LockedUntil = null;
        if (verified == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, input.Password);
        await db.SaveChangesAsync(ct); return user;
    }
    public async Task SignInAsync(HttpContext context, Guid id, bool remember, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == id, ct);
        if (user.Status != "Active") throw Error(403, "ACCOUNT_SUSPENDED", "Cuenta suspendida.");
        var session = new PortalSession { UserId = id, Stamp = user.SecurityStamp, ExpiresAt = remember ? Now.AddDays(14) : Now.AddHours(8) };
        db.Sessions.Add(session); await db.SaveChangesAsync(ct);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim("portal-session", session.Id.ToString())], PortalSecurity.CookieScheme);
        await context.SignInAsync(PortalSecurity.CookieScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = remember, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });
    }
    public async Task LogoutAsync(HttpContext context, CancellationToken ct)
    {
        if (Guid.TryParse(context.User.FindFirstValue("portal-session"), out var sessionId))
        {
            var userId = PortalSecurity.UserId(context);
            await db.Sessions.Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, Now), ct);
        }
        await context.SignOutAsync(PortalSecurity.CookieScheme);
    }
    private async Task QueueTokenAsync(PortalUser user, string purpose, CancellationToken ct)
    {
        var prior = await db.Tokens.Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null).ToListAsync(ct);
        foreach (var t in prior) t.UsedAt = Now;
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.Tokens.Add(new PortalToken { UserId = user.Id, Purpose = purpose, Hash = PortalSecurity.Hash(secret), ExpiresAt = purpose == "ConfirmEmail" ? Now.AddHours(24) : Now.AddHours(1), Stamp = user.SecurityStamp });
        var route = purpose == "ConfirmEmail" ? "/confirmar-correo" : "/restablecer-acceso";
        var link = options.PublicBaseUrl.TrimEnd('/') + route + "?userId=" + user.Id + "&token=" + Uri.EscapeDataString(secret);
        mail.Enqueue(user.Email, purpose == "ConfirmEmail" ? "Confirma tu cuenta web Envirapido" : "Recupera tu cuenta web Envirapido",
            "<p>Esta acción corresponde únicamente a tu cuenta del portal comercial, no al CRM.</p><p><a href=\"" + System.Net.WebUtility.HtmlEncode(link) + "\">Continuar</a></p><p>Si no lo solicitaste, ignora este mensaje.</p>");
    }
    public async Task ResendConfirmationAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleAsync(u => u.Id == id, ct);
        if (!user.EmailConfirmed) await QueueTokenAsync(user, "ConfirmEmail", ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ForgotPasswordAsync(PortalEmailInput input, CancellationToken ct)
    {
        var email = Email(input.Email);
        mail.EnsureAvailable(); // Misma respuesta de configuración para correos existentes o desconocidos.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == email, ct);
        if (user is not null) await QueueTokenAsync(user, "ResetPassword", ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task<PortalUser> ConsumeTokenAsync(Guid userId, string? token, string purpose, CancellationToken ct)
    {
        if (token is null || token.Length != 43) throw Error(400, "TOKEN_INVALID", "El enlace venció o ya no es válido.");
        var hash = PortalSecurity.Hash(token);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) throw Error(400, "TOKEN_INVALID", "El enlace venció o ya no es válido.");
        var consumed = await db.Tokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.Hash == hash && t.UsedAt == null && t.ExpiresAt > Now && t.Stamp == user.SecurityStamp)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, Now), ct);
        if (consumed != 1) throw Error(400, "TOKEN_INVALID", "El enlace venció o ya no es válido.");
        return user;
    }
    public async Task ConfirmEmailAsync(PortalTokenInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await ConsumeTokenAsync(input.UserId, input.Token, "ConfirmEmail", ct);
        user.EmailConfirmed = true; user.Version = Guid.NewGuid().ToString("N");
        audit.Add(user.Id.ToString(), "EmailConfirmed", "PortalUser", user.Id.ToString(), "Correo del portal confirmado.");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ResetPasswordAsync(PortalResetInput input, CancellationToken ct)
    {
        Password(input.Password, input.ConfirmPassword);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await ConsumeTokenAsync(input.UserId, input.Token, "ResetPassword", ct);
        user.PasswordHash = hasher.HashPassword(user, input.Password); user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLogins = 0; user.LockedUntil = null; user.Version = Guid.NewGuid().ToString("N");
        await db.Sessions.Where(s => s.UserId == user.Id && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, Now), ct);
        audit.Add(user.Id.ToString(), "PasswordReset", "PortalUser", user.Id.ToString(), "Contraseña del portal renovada y sesiones anteriores revocadas.");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
