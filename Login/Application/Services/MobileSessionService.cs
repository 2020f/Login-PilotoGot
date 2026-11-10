using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Login.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Login.Application.Services;

public sealed record MobileLoginResponse(string Token, DateTime ExpiresAt, string Email,
    IList<string> Roles, string RefreshToken);

/// <summary>Sesiones revocables por dispositivo; reutiliza AspNetUserTokens de Identity.</summary>
public sealed class MobileSessionService(ApplicationDbContext db, UserManager<IdentityUser> users,
    IConfiguration config, TimeProvider clock)
{
    private const string Provider = "PilotGoMobile";
    private sealed record SessionData(string Hash, DateTime ExpiresAt, string Stamp);

    public async Task<MobileLoginResponse> CreateAsync(IdentityUser user)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var (refresh, data) = NewRefresh(user, sessionId);
        db.UserTokens.Add(new IdentityUserToken<string>
        {
            UserId = user.Id, LoginProvider = Provider, Name = sessionId, Value = JsonSerializer.Serialize(data)
        });
        await db.SaveChangesAsync();
        return await ResponseAsync(user, sessionId, refresh);
    }

    public async Task<MobileLoginResponse?> RefreshAsync(string refresh)
    {
        var parts = Parse(refresh);
        if (parts is null) return null;
        var (userId, sessionId) = parts.Value;
        var stored = await db.UserTokens.AsNoTracking().SingleOrDefaultAsync(t =>
            t.UserId == userId && t.LoginProvider == Provider && t.Name == sessionId);
        var data = Read(stored?.Value);
        var user = await users.FindByIdAsync(userId);
        if (stored is null || data is null || user is null || data.ExpiresAt <= clock.GetUtcNow().UtcDateTime
            || data.Stamp != user.SecurityStamp || !Matches(refresh, data.Hash)
            || await users.IsLockedOutAsync(user)) return null;

        var (nextRefresh, nextData) = NewRefresh(user, sessionId);
        var nextValue = JsonSerializer.Serialize(nextData);
        var updated = await db.UserTokens.Where(t => t.UserId == userId && t.LoginProvider == Provider
                && t.Name == sessionId && t.Value == stored.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Value, nextValue));
        return updated == 1 ? await ResponseAsync(user, sessionId, nextRefresh) : null;
    }

    public async Task RevokeAsync(string refresh)
    {
        var parts = Parse(refresh);
        if (parts is null) return;
        var (userId, sessionId) = parts.Value;
        var stored = await db.UserTokens.AsNoTracking().SingleOrDefaultAsync(t =>
            t.UserId == userId && t.LoginProvider == Provider && t.Name == sessionId);
        var data = Read(stored?.Value);
        if (stored is null || data is null || !Matches(refresh, data.Hash)) return;
        await db.UserTokens.Where(t => t.UserId == userId && t.LoginProvider == Provider
            && t.Name == sessionId && t.Value == stored.Value).ExecuteDeleteAsync();
    }

    public async Task<bool> ValidateAsync(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return false;
        var user = await users.FindByIdAsync(id);
        if (user is null || await users.IsLockedOutAsync(user)) return false;
        var roles = await users.GetRolesAsync(user);
        if (!roles.ToHashSet(StringComparer.Ordinal).SetEquals(principal.FindAll(ClaimTypes.Role).Select(c => c.Value)))
            return false;
        var sessionId = principal.FindFirstValue("session_id");
        // Compatibilidad con tokens anteriores a esta versión, hasta su vencimiento.
        if (sessionId is null) return true;
        var value = await db.UserTokens.AsNoTracking().Where(t => t.UserId == id
            && t.LoginProvider == Provider && t.Name == sessionId).Select(t => t.Value).SingleOrDefaultAsync();
        var data = Read(value);
        return data is not null && data.ExpiresAt > clock.GetUtcNow().UtcDateTime
            && data.Stamp == user.SecurityStamp && principal.FindFirstValue("security_stamp") == user.SecurityStamp;
    }

    private (string Refresh, SessionData Data) NewRefresh(IdentityUser user, string sessionId)
    {
        var refresh = $"{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(user.Id))}.{sessionId}.{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
        var days = int.TryParse(config["Jwt:RefreshDays"], out var value) ? Math.Clamp(value, 1, 365) : 30;
        return (refresh, new SessionData(Hash(refresh), clock.GetUtcNow().UtcDateTime.AddDays(days), user.SecurityStamp ?? ""));
    }

    private async Task<MobileLoginResponse> ResponseAsync(IdentityUser user, string sessionId, string refresh)
    {
        var roles = await users.GetRolesAsync(user);
        var now = clock.GetUtcNow().UtcDateTime;
        var minutes = int.TryParse(config["Jwt:ExpireMinutes"], out var value) ? Math.Clamp(value, 1, 1440) : 60;
        var expires = now.AddMinutes(minutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id), new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? ""), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("session_id", sessionId), new("security_stamp", user.SecurityStamp ?? "")
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));
        var key = config["Jwt:Key"] ?? "PilotGo_Dev_Only_Secret_Key_ChangeMe_1234567890";
        var token = new JwtSecurityToken(config["Jwt:Issuer"] ?? "PilotGo", config["Jwt:Audience"] ?? "PilotGoMobile",
            claims, now, expires, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new MobileLoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user.Email ?? "", roles, refresh);
    }

    private static (string UserId, string SessionId)? Parse(string? refresh)
    {
        if (string.IsNullOrWhiteSpace(refresh) || refresh.Length > 1024) return null;
        var parts = refresh.Split('.');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[1], "N", out _) || parts[2].Length != 43) return null;
        try { return (Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[0])), parts[1]); }
        catch (FormatException) { return null; }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool Matches(string token, string hash) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(hash));
    private static SessionData? Read(string? value)
    {
        try { return value is null ? null : JsonSerializer.Deserialize<SessionData>(value); }
        catch (JsonException) { return null; }
    }
}
