namespace Envirapido.Portal.Api.Data;

public sealed class PortalUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool EmailConfirmed { get; set; }
    public string Status { get; set; } = "Active";
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public string TermsVersion { get; set; } = "";
    public string PrivacyVersion { get; set; } = "";
}
public sealed class PortalProfile
{
    public Guid UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string ResponsibleName { get; set; } = "";
    public string BusinessName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Province { get; set; } = "";
    public string City { get; set; } = "";
    public string OperationType { get; set; } = "";
    public int? EstimatedStores { get; set; }
    public int? EstimatedPilots { get; set; }
    public int? EstimatedOrdersPerMonth { get; set; }
    public string? Rnc { get; set; }
    public string? BillingAddress { get; set; }
    public string? Notes { get; set; }
}
public sealed class PortalSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string Stamp { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class PortalToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string Purpose { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string Stamp { get; set; } = "";
}
public sealed class PortalCatalogEntry
{
    public string Code { get; set; } = "";
    public bool IsPublished { get; set; }
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Featured { get; set; }
    public string LastPlanSnapshotJson { get; set; } = "";
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed class PortalPurchaseRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public string PlanSnapshotJson { get; set; } = "";
    public string ProfileSnapshotJson { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "DOP";
    public string Status { get; set; } = "Submitted";
    public string? PublicMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public List<PortalPaymentReport> PaymentReports { get; set; } = [];
    public List<PortalPayment> Payments { get; set; } = [];
    public List<PortalApproval> Decisions { get; set; } = [];
}
public sealed class PortalPaymentReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public PortalPurchaseRequest Request { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }
    public string Method { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = "Reported";
    public string? Resolution { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed class PortalPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid? ReportId { get; set; }
    public PortalPurchaseRequest Request { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }
    public string Method { get; set; } = "";
    public string Reference { get; set; } = "";
    public string ReferenceKey { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed class PortalApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public PortalPurchaseRequest Request { get; set; } = null!;
    public string Decision { get; set; } = "";
    public string Message { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class PortalAccessGrant
{
    public Guid UserId { get; set; }
    public PortalUser User { get; set; } = null!;
    public Guid RequestId { get; set; }
    public PortalPurchaseRequest Request { get; set; } = null!;
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string Status { get; set; } = "Enabled";
    public string Reason { get; set; } = "";
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed class PortalAuditEvent
{
    public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class PortalOperation
{
    public long Id { get; set; }
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    public string Hash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class PortalOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProtectedMessage { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessingUntil { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
