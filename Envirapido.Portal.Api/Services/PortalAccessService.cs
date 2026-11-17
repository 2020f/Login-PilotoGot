using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Envirapido.Portal.Api.Services;

public sealed class PortalAccessService(PortalDbContext db, TimeProvider clock)
{
    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().ToOffset(TimeSpan.FromHours(-4)).DateTime);
    public static DateTimeOffset Start(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-4)).ToUniversalTime();
    public async Task<PortalAccessDto> GetAccessAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        var grant = await db.AccessGrants.AsNoTracking().Include(g => g.Request).ThenInclude(r => r.Payments).SingleOrDefaultAsync(g => g.UserId == userId, ct);
        return Evaluate(user, grant, clock.GetUtcNow());
    }
    internal static PortalAccessDto Evaluate(PortalUser? user, PortalAccessGrant? grant, DateTimeOffset now)
    {
        if (grant is not null && (grant.StartsOn.Year is < 2000 or > 2100 || grant.EndsOn.Year is < 2000 or > 2100 || grant.EndsOn < grant.StartsOn))
            return new(false, "NOT_CONFIGURED", null, null, null);
        DateTimeOffset? start = grant is null ? null : Start(grant.StartsOn);
        DateTimeOffset? end = grant is null ? null : Start(grant.EndsOn.AddDays(1));
        string reason;
        if (user is null || user.Status != "Active") reason = "ACCOUNT_SUSPENDED";
        else if (!user.EmailConfirmed) reason = "EMAIL_UNCONFIRMED";
        else if (grant is null) reason = "NOT_CONFIGURED";
        else if (grant.Status == "Revoked") reason = "REVOKED";
        else if (grant.Status != "Enabled") reason = "SUSPENDED";
        else if (grant.Request.UserId != user.Id || grant.Request.Status != "Approved") reason = "REQUEST_PENDING";
        else if (grant.Request.Payments.Where(p => p.VoidedAt == null).Sum(p => p.Amount) < grant.Request.Amount) reason = "PAYMENT_PENDING";
        else if (now < start) reason = "NOT_STARTED";
        else if (now >= end) reason = "EXPIRED";
        else reason = "ACTIVE";
        return new(reason == "ACTIVE", reason, start, end, reason == "ACTIVE" ? PortalOptions.CrmLoginUrl : null);
    }
}
