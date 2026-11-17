using Envirapido.Portal.Api.Data;

namespace Envirapido.Portal.Api.Services;

public sealed class PortalAuditService(PortalDbContext db, TimeProvider clock)
{
    public void Add(string actor, string action, string entity, string entityId, string detail)
    {
        if (actor.Length is < 1 or > 150 || action.Length is < 1 or > 80 || entity.Length > 80 || entityId.Length > 100 || detail.Length > 2000)
            throw new InvalidOperationException("Campos de auditoría inválidos.");
        db.AuditEvents.Add(new PortalAuditEvent { Actor = actor, Action = action, Entity = entity, EntityId = entityId, Detail = detail, CreatedAt = clock.GetUtcNow() });
    }
    public async Task RecordAsync(string actor, string action, string entity, string entityId, string detail, CancellationToken ct = default)
    { Add(actor, action, entity, entityId, detail); await db.SaveChangesAsync(ct); }
}
