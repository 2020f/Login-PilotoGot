using System.ComponentModel.DataAnnotations;
using Login.Domain.Entities;

namespace Login.Domain.Saas;

// Fechas comerciales sin hora: calendario de Santo Domingo. La auditoría usa UTC.
public sealed class SaasInvoice
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public ClienteApp ClienteApp { get; set; } = null!;
    [MaxLength(200)] public string Concept { get; set; } = "";
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "DOP";
    public DateTime CreatedAt { get; set; }
    [MaxLength(150)] public string CreatedBy { get; set; } = "";
    public DateTime? VoidedAt { get; set; }
    [MaxLength(500)] public string? VoidReason { get; set; }
    [MaxLength(80)] public string IdempotencyKey { get; set; } = "";
    public ICollection<SaasPayment> Payments { get; set; } = new List<SaasPayment>();
}

public sealed class SaasPayment
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public int InvoiceId { get; set; }
    public SaasInvoice Invoice { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime PaidOn { get; set; }
    [MaxLength(30)] public string Method { get; set; } = "";
    [MaxLength(100)] public string Reference { get; set; } = "";
    [MaxLength(140)] public string ReferenceKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    [MaxLength(150)] public string CreatedBy { get; set; } = "";
    public DateTime? VoidedAt { get; set; }
    [MaxLength(500)] public string? VoidReason { get; set; }
    [MaxLength(80)] public string IdempotencyKey { get; set; } = "";
}

public sealed class SaasSubscription
{
    public int ClienteAppId { get; set; }
    public ClienteApp ClienteApp { get; set; } = null!;
    public DateTime? BaselineStart { get; set; }
    public DateTime? BaselineEnd { get; set; }
    public bool BaselineTrial { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SaasPlanRequest
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public ClienteApp ClienteApp { get; set; } = null!;
    public int RequestedPlanId { get; set; }
    public Plan RequestedPlan { get; set; } = null!;
    public int PreviousPlanId { get; set; }
    [MaxLength(500)] public string Reason { get; set; } = "";
    [MaxLength(150)] public string RequestedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Pendiente";
    public DateTime? ResolvedAt { get; set; }
    [MaxLength(150)] public string? ResolvedBy { get; set; }
    [MaxLength(500)] public string? Resolution { get; set; }
    [MaxLength(80)] public string IdempotencyKey { get; set; } = "";
    [MaxLength(80)] public string? ResolutionKey { get; set; }
    public bool? ResolutionResetOverrides { get; set; }
}

public sealed class SaasAuditLog
{
    public long Id { get; set; }
    public int? ClienteAppId { get; set; }
    public ClienteApp? ClienteApp { get; set; }
    [MaxLength(80)] public string Action { get; set; } = "";
    [MaxLength(60)] public string Entity { get; set; } = "";
    [MaxLength(100)] public string EntityId { get; set; } = "";
    [MaxLength(2000)] public string Detail { get; set; } = "";
    [MaxLength(500)] public string? Before { get; set; }
    [MaxLength(500)] public string? After { get; set; }
    [MaxLength(150)] public string Actor { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    [MaxLength(80)] public string? OperationKey { get; set; }
}
