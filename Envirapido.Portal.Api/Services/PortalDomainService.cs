using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Security;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Envirapido.Portal.Api.Services;

public sealed partial class PortalDomainService
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly PortalDbContext db;
    private readonly PortalCatalogService catalog;
    private readonly PortalAccessService access;
    private readonly PortalAuditService audit;
    private readonly PortalMailService mail;
    private readonly IPasswordHasher<PortalUser> hasher;
    private readonly PortalOptions options;
    private readonly TimeProvider clock;
    private DateTimeOffset Now => clock.GetUtcNow();
    private DateOnly Today => PortalAccessService.Today(clock);
    public PortalDomainService(PortalDbContext db, PortalCatalogService catalog, PortalAccessService access, PortalAuditService audit,
        PortalMailService mail, IPasswordHasher<PortalUser> hasher, PortalOptions options, TimeProvider clock)
    { this.db = db; this.catalog = catalog; this.access = access; this.audit = audit; this.mail = mail; this.hasher = hasher; this.options = options; this.clock = clock; }

    public async Task<T> IdempotentAsync<T>(string scope, string key, object request, Func<Task<T>> mutation, CancellationToken ct)
    {
        if (!Guid.TryParse(key, out var parsed)) throw Error(400, "OPERATION_KEY_REQUIRED", "Identificador de operación inválido.");
        key = parsed.ToString("N");
        var hash = PortalSecurity.Hash(JsonSerializer.Serialize(request, Json));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var previous = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.Scope == scope && x.Key == key, ct);
        if (previous is not null)
        {
            if (previous.Hash != hash) throw Error(409, "OPERATION_CONFLICT", "Esta operación ya se utilizó con otros datos.");
            await tx.CommitAsync(ct);
            return JsonSerializer.Deserialize<T>(previous.ResponseJson, Json)!;
        }
        var result = await mutation();
        db.Operations.Add(new PortalOperation { Scope = scope, Key = key, Hash = hash, ResponseJson = JsonSerializer.Serialize(result, Json), CreatedAt = Now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }
    public static PortalException Error(int status, string code, string text) => new(status, code, text);
    public static string Text(string? text, int max, string label)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > max || text.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
            throw Error(400, "VALIDATION", $"{label}: campo requerido de hasta {max} caracteres.");
        return text;
    }
    private static string? Optional(string? text, int max, string label) => string.IsNullOrWhiteSpace(text) ? null : Text(text, max, label);
    private static void Version(string current, string? expected)
    { if (current != expected) throw Error(409, "VERSION_CONFLICT", "Los datos cambiaron. Actualiza la vista antes de continuar."); }
    private static void Password(string? password, string? confirm)
    {
        if (password is null || password.Length is < 10 or > 100 || password != confirm)
            throw Error(400, "VALIDATION", "La contraseña debe coincidir con su confirmación y tener entre 10 y 100 caracteres.");
    }
    private static string Email(string? email)
    {
        email = Text(email, 256, "Correo").ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(email)) throw Error(400, "VALIDATION", "Escribe un correo válido.");
        return email;
    }
    public static PortalProfileInput Profile(PortalProfile p) => new(p.ResponsibleName, p.BusinessName, p.Phone, p.Province, p.City, p.OperationType,
        p.EstimatedStores, p.EstimatedPilots, p.EstimatedOrdersPerMonth, p.Rnc, p.BillingAddress, p.Notes);
    private static PortalProfileInput CheckProfile(PortalProfileInput? p)
    {
        if (p is null) throw Error(400, "VALIDATION", "Completa los datos del perfil.");
        if (new[] { p.EstimatedStores, p.EstimatedPilots, p.EstimatedOrdersPerMonth }.Any(n => n is < 0 or > 10000000))
            throw Error(400, "VALIDATION", "Las cantidades estimadas no son válidas.");
        return new(Text(p.ResponsibleName, 120, "Responsable"), Text(p.BusinessName, 160, "Empresa"), Text(p.Phone, 30, "Teléfono"),
            Text(p.Province, 100, "Provincia"), Text(p.City, 100, "Ciudad"), Text(p.OperationType, 100, "Operación"), p.EstimatedStores, p.EstimatedPilots,
            p.EstimatedOrdersPerMonth, Optional(p.Rnc, 30, "RNC"), Optional(p.BillingAddress, 300, "Dirección"), Optional(p.Notes, 1000, "Observaciones"));
    }
    private static void SetProfile(PortalProfile p, PortalProfileInput input)
    {
        p.ResponsibleName = input.ResponsibleName; p.BusinessName = input.BusinessName; p.Phone = input.Phone; p.Province = input.Province; p.City = input.City;
        p.OperationType = input.OperationType; p.EstimatedStores = input.EstimatedStores; p.EstimatedPilots = input.EstimatedPilots;
        p.EstimatedOrdersPerMonth = input.EstimatedOrdersPerMonth; p.Rnc = input.Rnc; p.BillingAddress = input.BillingAddress; p.Notes = input.Notes;
    }
    public static PortalRequestDto RequestDto(PortalPurchaseRequest r)
    {
        var paid = r.Payments.Where(p => p.VoidedAt == null).Sum(p => p.Amount);
        var status = r.Amount == 0 ? "NotRequired" : paid >= r.Amount ? "Confirmed" : r.PaymentReports.Any(p => p.Status == "InReview") ? "InReview"
            : r.PaymentReports.Any(p => p.Status == "Reported") || paid > 0 ? "Reported" : r.PaymentReports.Any(p => p.Status == "Rejected") ? "Rejected" : r.Payments.Count > 0 ? "Voided" : "NotReported";
        var reviewMessage = status is "InReview" or "Rejected" ? r.PaymentReports.Where(p => p.Status == status).OrderByDescending(p => p.ResolvedAt ?? p.CreatedAt).Select(p => p.Resolution).FirstOrDefault() : null;
        return new(r.Id, r.CreatedAt, JsonSerializer.Deserialize<PortalPlanDto>(r.PlanSnapshotJson, Json)!, r.Status, r.PublicMessage, status, paid, Math.Max(0, r.Amount - paid), r.Version, reviewMessage);
    }
    private async Task<PortalPurchaseRequest> RequestAsync(Guid id, Guid? owner, CancellationToken ct)
        => await db.Requests.Include(r => r.Payments).Include(r => r.PaymentReports).Include(r => r.Decisions)
            .SingleOrDefaultAsync(r => r.Id == id && (owner == null || r.UserId == owner), ct)
            ?? throw Error(404, "NOT_FOUND", "Solicitud no disponible.");
    public async Task<PortalAccountDto> AccountAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw Error(401, "SESSION_REQUIRED", "Inicia sesión.");
        var profile = await db.Profiles.AsNoTracking().SingleAsync(p => p.UserId == id, ct);
        return new(new(user.Id, user.Email, user.EmailConfirmed, user.Status), Profile(profile), await access.GetAccessAsync(id, ct), await RequestsAsync(id, ct));
    }
    public async Task<IReadOnlyList<PortalRequestDto>> RequestsAsync(Guid id, CancellationToken ct = default)
        => (await db.Requests.AsNoTracking().Include(r => r.Payments).Include(r => r.PaymentReports).Where(r => r.UserId == id).OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync(ct)).Select(RequestDto).ToList();
    public async Task<PortalAccountDto> UpdateProfileAsync(Guid id, PortalProfileInput input, CancellationToken ct)
    {
        input = CheckProfile(input); var p = await db.Profiles.SingleAsync(p => p.UserId == id, ct); SetProfile(p, input);
        audit.Add(id.ToString(), "ProfileUpdated", "PortalUser", id.ToString(), "Perfil comercial actualizado.");
        await db.SaveChangesAsync(ct); return await AccountAsync(id, ct);
    }
    private async Task<PortalPurchaseRequest> NewRequestAsync(Guid id, string code, string version, CancellationToken ct)
    {
        if (await db.Requests.AnyAsync(r => r.UserId == id && (r.Status == "Submitted" || r.Status == "InReview" || r.Status == "NeedsInformation"), ct))
            throw Error(409, "REQUEST_PENDING", "Ya tienes una solicitud pendiente de revisión.");
        var plan = await catalog.ForPurchaseAsync(code, version, ct);
        var profile = await db.Profiles.SingleAsync(p => p.UserId == id, ct);
        var request = new PortalPurchaseRequest { UserId = id, PlanSnapshotJson = JsonSerializer.Serialize(plan, Json), ProfileSnapshotJson = JsonSerializer.Serialize(Profile(profile), Json), Amount = plan.MonthlyPrice, Currency = plan.Currency, CreatedAt = Now };
        db.Requests.Add(request); audit.Add(id.ToString(), "RequestSubmitted", "PortalRequest", request.Id.ToString(), $"Solicitud de {plan.Code}; {plan.Currency} {plan.MonthlyPrice:0.00}.");
        return request;
    }
    public Task<PortalRequestDto> SubmitRequestAsync(Guid id, PortalRequestInput input, string key, CancellationToken ct)
        => IdempotentAsync("request:" + id, key, input, async () => RequestDto(await NewRequestAsync(id, input.PlanCode, input.PlanVersion, ct)), ct);
    private static void Money(decimal amount)
    { if (amount <= 0 || amount > 999999999.99m || decimal.Round(amount, 2) != amount) throw Error(400, "VALIDATION", "El importe debe ser positivo y tener hasta dos decimales."); }
    private void PaymentFields(decimal amount, DateOnly paidOn, string method, string reference)
    {
        Money(amount);
        if (paidOn > Today || paidOn.Year < 2000 || method is not ("Transferencia" or "Efectivo" or "Tarjeta" or "Otro")) throw Error(400, "VALIDATION", "Fecha real o método de pago inválido.");
        Text(reference, 100, "Referencia");
    }
    public Task<PortalPaymentReportDto> ReportPaymentAsync(Guid id, Guid requestId, PortalPaymentReportInput input, string key, CancellationToken ct)
        => IdempotentAsync("report:" + id, key, new { requestId, input }, async () =>
        {
            PaymentFields(input.Amount, input.PaidOn, input.Method, input.Reference);
            var request = await RequestAsync(requestId, id, ct);
            if (request.Status is "Rejected" or "Cancelled" || input.Amount > RequestDto(request).AmountDue) throw Error(409, "PAYMENT_CONFLICT", "La solicitud no admite este reporte de pago.");
            var report = new PortalPaymentReport { RequestId = requestId, Amount = input.Amount, PaidOn = input.PaidOn, Method = input.Method,
                Reference = input.Reference.Trim(), Notes = Optional(input.Notes, 500, "Nota"), CreatedAt = Now };
            request.PaymentReports.Add(report); db.PaymentReports.Add(report); request.Version = Guid.NewGuid().ToString("N");
            audit.Add(id.ToString(), "PaymentReported", "PortalRequest", requestId.ToString(), "Reporte recibido; requiere comprobación manual, no confirma un pago.");
            return ReportDto(report);
        }, ct);
    private static PortalPaymentReportDto ReportDto(PortalPaymentReport report) => new(report.Id, report.Amount, report.PaidOn, report.Method, report.Reference, report.Notes, report.CreatedAt, report.Status, report.Resolution, report.Version);
}
