using Envirapido.Portal.Contracts;

namespace Login.ViewModels.SuperAdmin;

public sealed class PortalAdminVm
{
    public string Tab { get; set; } = "Solicitudes";
    public string Search { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Configured { get; set; }
    public string? Error { get; set; }
    public string? Ok { get; set; }
    public PortalAdminSummary? Summary { get; set; }
    public PortalPage<PortalAdminUserDto>? Users { get; set; }
    public PortalPage<PortalAdminRequestDto>? Requests { get; set; }
    public PortalAdminRequestDetail? Detail { get; set; }
    public IReadOnlyList<PortalAdminCatalogEntry> Catalog { get; set; } = [];
    public PortalPage<PortalAuditDto>? Audit { get; set; }
    public PortalArtifactListDto? Artifacts { get; set; }
    public DateOnly Today { get; set; }
    public static readonly string[] Tabs = ["Solicitudes", "Compradores", "Catálogo web", "Versiones Android", "Auditoría"];
    public static string Label(string value) => value switch
    {
        "Submitted" => "Enviada", "InReview" => "En revisión", "NeedsInformation" => "Requiere información",
        "Approved" => "Aprobada", "Rejected" => "Rechazada", "Cancelled" => "Cancelada", "Active" => "Activa",
        "Suspended" => "Suspendida", "Confirmed" => "Confirmado", "NotReported" => "Sin reportar",
        "Reported" => "Reportado", "NotRequired" => "No requerido", "Voided" => "Anulado", "Enabled" => "Habilitado",
        "Revoked" => "Revocado", "Quarantined" => "En cuarentena", "Validated" => "Validado",
        "Published" => "Publicado", "Withdrawn" => "Retirado", _ => value
    };
}

public sealed record PortalCatalogAdminResult(IReadOnlyList<PortalAdminCatalogEntry> Plans);
