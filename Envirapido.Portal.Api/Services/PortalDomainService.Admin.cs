using System.Text.Json;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Envirapido.Portal.Api.Services;

public sealed partial class PortalDomainService
{
    private static PortalAdminUserDto AdminUser(PortalUser u, string business) => new(u.Id, u.Email, u.EmailConfirmed, u.Status, business, u.CreatedAt, u.Version);
    public async Task<PortalAdminSummary> SummaryAsync(CancellationToken ct)
    {
        var grants = await db.AccessGrants.AsNoTracking().Include(g => g.User).Include(g => g.Request).ThenInclude(r => r.Payments).ToListAsync(ct);
        return new(await db.Users.CountAsync(ct), await db.Requests.CountAsync(ct),
            await db.Requests.CountAsync(r => r.Status == "Submitted" || r.Status == "InReview" || r.Status == "NeedsInformation", ct),
            await db.PaymentReports.CountAsync(r => r.Status == "Reported" || r.Status == "InReview", ct),
            grants.Count(g => PortalAccessService.Evaluate(g.User, g, Now).Allowed),
            grants.Count(g => g.StartsOn.Year is >= 2000 and <= 2100 && g.EndsOn.Year is >= 2000 and <= 2100 && g.EndsOn >= g.StartsOn && PortalAccessService.Start(g.EndsOn.AddDays(1)) <= Now), await db.CatalogEntries.CountAsync(p => p.IsPublished, ct));
    }
    public async Task<PortalPage<PortalAdminUserDto>> AdminUsersAsync(string? q, string? status, int page, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100000); q = Optional(q, 150, "Búsqueda");
        var query = db.Users.AsNoTracking().AsQueryable();
        if (q is not null) query = query.Where(u => u.Email.Contains(q) || db.Profiles.Any(p => p.UserId == u.Id && p.BusinessName.Contains(q)));
        if (!string.IsNullOrEmpty(status)) query = query.Where(u => u.Status == status);
        var total = await query.CountAsync(ct);
        var users = await query.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToArray(); var profiles = await db.Profiles.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        return new(users.Select(u => AdminUser(u, profiles[u.Id].BusinessName)).ToList(), total, page, 20);
    }
    public Task<PortalAdminUserDto> SetUserStatusAsync(Guid id, PortalAdminUserStatusInput input, string actor, string key, CancellationToken ct)
        => IdempotentAsync("admin-user:" + actor, key, new { id, input }, async () =>
        {
            if (input.Status is not ("Active" or "Suspended")) throw Error(400, "VALIDATION", "Estado de cuenta inválido.");
            var reason = Text(input.Reason, 500, "Motivo"); var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw Error(404, "NOT_FOUND", "Cuenta no disponible.");
            Version(user.Version, input.Version); user.Status = input.Status; user.SecurityStamp = Guid.NewGuid().ToString("N"); user.Version = Guid.NewGuid().ToString("N");
            await db.Sessions.Where(s => s.UserId == id && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, Now), ct);
            audit.Add(actor, "UserStatusChanged", "PortalUser", id.ToString(), input.Status + "; " + reason);
            return AdminUser(user, (await db.Profiles.SingleAsync(p => p.UserId == id, ct)).BusinessName);
        }, ct);
    public async Task<PortalPage<PortalAdminRequestDto>> AdminRequestsAsync(string? q, string? status, int page, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100000); q = Optional(q, 150, "Búsqueda");
        var query = db.Requests.AsNoTracking().Include(r => r.Payments).Include(r => r.PaymentReports).Include(r => r.User).AsQueryable();
        if (q is not null) query = query.Where(r => r.User.Email.Contains(q) || db.Profiles.Any(p => p.UserId == r.UserId && p.BusinessName.Contains(q)));
        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        var total = await query.CountAsync(ct); var rows = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var ids = rows.Select(r => r.UserId).ToArray(); var profiles = await db.Profiles.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        return new(rows.Select(r => new PortalAdminRequestDto(RequestDto(r), r.UserId, r.User.Email, profiles[r.UserId].BusinessName)).ToList(), total, page, 20);
    }
    public async Task<PortalAdminRequestDetail> AdminDetailAsync(Guid id, CancellationToken ct)
    {
        var r = await RequestAsync(id, null, ct); var user = await db.Users.SingleAsync(u => u.Id == r.UserId, ct);
        var profile = await db.Profiles.SingleAsync(p => p.UserId == r.UserId, ct);
        var grant = await db.AccessGrants.AsNoTracking().SingleOrDefaultAsync(g => g.UserId == r.UserId && g.RequestId == id, ct);
        return new(RequestDto(r), AdminUser(user, profile.BusinessName), JsonSerializer.Deserialize<PortalProfileInput>(r.ProfileSnapshotJson, Json)!,
            r.PaymentReports.OrderByDescending(p => p.CreatedAt).Select(ReportDto).ToList(),
            r.Payments.OrderByDescending(p => p.CreatedAt).Select(PaymentDto).ToList(),
            r.Decisions.OrderByDescending(p => p.CreatedAt).Select(p => new PortalDecisionDto(p.Id, p.Decision, p.Message, p.Actor, p.CreatedAt)).ToList(),
            grant is null ? null : GrantDto(grant), Profile(profile));
    }
    public Task<PortalRequestDto> DecideAsync(Guid id, PortalDecisionInput input, string actor, string key, CancellationToken ct)
        => IdempotentAsync("admin-decision:" + actor, key, new { id, input }, async () =>
        {
            if (input.Decision is not ("InReview" or "NeedsInformation" or "Approved" or "Rejected" or "Cancelled")) throw Error(400, "VALIDATION", "Decisión inválida.");
            var message = Text(input.Message, 500, "Resolución pública"); var request = await RequestAsync(id, null, ct); Version(request.Version, input.Version);
            if (request.Status is "Rejected" or "Cancelled" || request.Status == "Approved" && input.Decision != "Cancelled")
                throw Error(409, "REQUEST_RESOLVED", "La solicitud ya fue resuelta; inicia una solicitud nueva para otro período.");
            request.Status = input.Decision; request.PublicMessage = message; request.Version = Guid.NewGuid().ToString("N");
            var decision = new PortalApproval { RequestId = id, Decision = input.Decision, Message = message, Actor = actor, CreatedAt = Now };
            request.Decisions.Add(decision); db.Approvals.Add(decision);
            audit.Add(actor, "RequestDecision", "PortalRequest", id.ToString(), input.Decision + "; " + message);
            // Esta decisión no confirma dinero, no concede fechas y no crea un tenant.
            return RequestDto(request);
        }, ct);
    private static PortalPaymentDto PaymentDto(PortalPayment p) => new(p.Id, p.Amount, p.PaidOn, p.Method, p.Reference, p.CreatedAt, p.VoidedAt, p.Version);
    public Task<PortalPaymentReportDto> DecidePaymentReportAsync(Guid id, PortalPaymentReportDecisionInput input, string actor, string key, CancellationToken ct)
        => IdempotentAsync("admin-report-decision:" + actor, key, new { id, input }, async () =>
        {
            if (input.Decision is not ("InReview" or "Rejected")) throw Error(400, "VALIDATION", "La revisión de un reporte no puede confirmar dinero.");
            var report = await db.PaymentReports.Include(r => r.Request).SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw Error(404, "NOT_FOUND", "Reporte no disponible.");
            Version(report.Version, input.Version);
            if (report.Status is "Confirmed" or "Voided") throw Error(409, "REPORT_RESOLVED", "El reporte tiene una confirmación financiera; utiliza el flujo de pago/anulación.");
            report.Resolution = Text(input.Reason, 500, "Motivo"); report.Status = input.Decision; report.ResolvedBy = actor; report.ResolvedAt = Now; report.Version = Guid.NewGuid().ToString("N");
            report.Request.Version = Guid.NewGuid().ToString("N");
            audit.Add(actor, "PaymentReportDecision", "PortalPaymentReport", id.ToString(), input.Decision + "; " + report.Resolution);
            return ReportDto(report);
        }, ct);
    public Task<PortalPaymentDto> ConfirmPaymentAsync(PortalPaymentConfirmInput input, string actor, string key, CancellationToken ct)
        => IdempotentAsync("admin-payment:" + actor, key, input, async () =>
        {
            PaymentFields(input.Amount, input.PaidOn, input.Method, input.Reference); var reason = Text(input.Reason, 500, "Motivo de confirmación");
            var request = await RequestAsync(input.RequestId, null, ct); Version(request.Version, input.Version);
            if (request.Status is "Cancelled" or "Rejected" || input.Amount > RequestDto(request).AmountDue) throw Error(409, "PAYMENT_CONFLICT", "La solicitud no admite este pago.");
            var reference = input.Reference.Trim(); var referenceKey = input.Method.ToUpperInvariant() + ":" + reference.ToUpperInvariant();
            if (await db.Payments.AnyAsync(p => p.ReferenceKey == referenceKey, ct)) throw Error(409, "REFERENCE_USED", "La referencia ya está registrada en el portal.");
            if (input.ReportId.HasValue)
            {
                var report = request.PaymentReports.SingleOrDefault(r => r.Id == input.ReportId) ?? throw Error(404, "NOT_FOUND", "El reporte no pertenece a esta solicitud.");
                if (report.Status is "Rejected" or "Confirmed" or "Voided") throw Error(409, "REPORT_RESOLVED", "Este reporte no admite confirmación; revisa su estado.");
                report.Status = "Confirmed"; report.Resolution = reason; report.ResolvedBy = actor; report.ResolvedAt = Now; report.Version = Guid.NewGuid().ToString("N");
            }
            var payment = new PortalPayment { RequestId = request.Id, ReportId = input.ReportId, Amount = input.Amount, PaidOn = input.PaidOn, Method = input.Method, Reference = reference,
                ReferenceKey = referenceKey, Actor = actor, Reason = reason, CreatedAt = Now };
            request.Payments.Add(payment); db.Payments.Add(payment); request.Version = Guid.NewGuid().ToString("N");
            audit.Add(actor, "PaymentConfirmed", "PortalPayment", payment.Id.ToString(), $"Solicitud {request.Id}; {request.Currency} {payment.Amount:0.00}; {reason}");
            return PaymentDto(payment);
        }, ct);
    public Task<PortalPaymentDto> VoidPaymentAsync(Guid id, PortalPaymentVoidInput input, string actor, string key, CancellationToken ct)
        => IdempotentAsync("admin-void-payment:" + actor, key, new { id, input }, async () =>
        {
            var payment = await db.Payments.Include(p => p.Request).SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Error(404, "NOT_FOUND", "Pago no disponible.");
            Version(payment.Version, input.Version); if (payment.VoidedAt != null) throw Error(409, "PAYMENT_VOIDED", "El pago ya está anulado.");
            payment.VoidReason = Text(input.Reason, 500, "Motivo"); payment.VoidedAt = Now; payment.Version = Guid.NewGuid().ToString("N"); payment.Request.Version = Guid.NewGuid().ToString("N");
            if (payment.ReportId.HasValue)
            {
                var report = await db.PaymentReports.SingleAsync(r => r.Id == payment.ReportId && r.RequestId == payment.RequestId, ct);
                report.Status = "Voided"; report.Resolution = payment.VoidReason; report.ResolvedBy = actor; report.ResolvedAt = Now; report.Version = Guid.NewGuid().ToString("N");
            }
            audit.Add(actor, "PaymentVoided", "PortalPayment", id.ToString(), payment.VoidReason); return PaymentDto(payment);
        }, ct);
    private static PortalAccessGrantDto GrantDto(PortalAccessGrant g) => new(g.UserId, g.RequestId, g.StartsOn, g.EndsOn, g.Status, g.Reason, g.Version);
    public Task<PortalAccessGrantDto> SetAccessAsync(PortalAccessInput input, string actor, string key, CancellationToken ct)
        => IdempotentAsync("admin-access:" + actor, key, input, async () =>
        {
            if (input.Status is not ("Enabled" or "Suspended" or "Revoked") || input.StartsOn.Year is < 2000 or > 2100 || input.EndsOn.Year is < 2000 or > 2100 || input.EndsOn < input.StartsOn)
                throw Error(400, "VALIDATION", "Revisa estado e intervalo de vigencia del portal.");
            var reason = Text(input.Reason, 500, "Motivo"); var request = await RequestAsync(input.RequestId, input.UserId, ct);
            if (request.Status != "Approved") throw Error(409, "REQUEST_PENDING", "Primero aprueba la solicitud de este comprador.");
            var grant = await db.AccessGrants.SingleOrDefaultAsync(g => g.UserId == input.UserId, ct);
            if (grant is null)
            {
                if (input.Version is not null) throw Error(409, "VERSION_CONFLICT", "La vigencia no existe; actualiza la vista.");
                grant = new PortalAccessGrant { UserId = input.UserId }; db.AccessGrants.Add(grant);
            }
            else Version(grant.Version, input.Version);
            grant.RequestId = request.Id; grant.StartsOn = input.StartsOn; grant.EndsOn = input.EndsOn; grant.Status = input.Status; grant.Reason = reason; grant.Version = Guid.NewGuid().ToString("N");
            audit.Add(actor, "AccessWindowChanged", "PortalUser", input.UserId.ToString(), $"{input.Status}; {input.StartsOn:yyyy-MM-dd} a {input.EndsOn:yyyy-MM-dd}; solicitud {request.Id}; {reason}");
            return GrantDto(grant);
        }, ct);
    public Task<PortalAdminCatalogEntry> PublishCatalogAsync(PortalCatalogPublicationInput input, string actor, string key, CancellationToken ct)
        => IdempotentAsync("admin-catalog:" + actor, key, input, async () =>
        {
            var code = Text(input.Code, 80, "Plan"); if (input.SortOrder is < -100000 or > 100000) throw Error(400, "VALIDATION", "Orden comercial inválido.");
            var entry = await db.CatalogEntries.SingleOrDefaultAsync(e => e.Code == code, ct);
            var source = input.IsPublished ? (await catalog.SourceAsync(true, ct)).Plans.SingleOrDefault(p => p.Code == code) : null;
            if (input.IsPublished && source is null) throw Error(409, "PLAN_UNAVAILABLE", "El plan de origen está inactivo o no existe.");
            if (entry is null)
            {
                if (!input.IsPublished) throw Error(404, "NOT_FOUND", "Esta publicación no existe.");
                if (input.Version is not null) throw Error(409, "VERSION_CONFLICT", "Actualiza el catálogo.");
                entry = new PortalCatalogEntry { Code = code }; db.CatalogEntries.Add(entry);
            }
            else Version(entry.Version, input.Version);
            entry.IsPublished = input.IsPublished; entry.Description = Optional(input.Description, 2000, "Descripción") ?? "";
            entry.SortOrder = input.SortOrder; entry.Featured = input.Featured; entry.Version = Guid.NewGuid().ToString("N");
            var plan = source is not null ? PortalCatalogService.Plan(source, entry) : PortalCatalogService.StoredPlan(entry);
            entry.LastPlanSnapshotJson = JsonSerializer.Serialize(plan, Json);
            audit.Add(actor, "CatalogPublicationChanged", "PortalCatalog", code, input.IsPublished ? "Plan publicado en portal; precio y límites conservan su fuente CRM." : "Plan retirado del catálogo web.");
            return new PortalAdminCatalogEntry(plan, entry.IsPublished, entry.SortOrder, entry.Version, source is not null);
        }, ct);
    public async Task<PortalPage<PortalAuditDto>> AuditAsync(int page, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100000);
        return new(await db.AuditEvents.AsNoTracking().OrderByDescending(a => a.Id).Skip((page - 1) * 30).Take(30)
            .Select(a => new PortalAuditDto(a.Id, a.Actor, a.Action, a.Entity, a.EntityId, a.Detail, a.CreatedAt)).ToListAsync(ct), await db.AuditEvents.CountAsync(ct), page, 30);
    }
}
