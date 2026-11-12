using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Saas;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services;

/// <summary>Registro de cobros externos. Nunca inicia transferencias ni da por pagada una fecha.</summary>
public sealed class SaasBillingService(ApplicationDbContext db, QuotaPolicyService quotas, TimeProvider clock)
{
    public DateTime Today => clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-4)).Date;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<bool> IsExpiredAsync(int tenantId)
        => (await GetExpiredStatesAsync(new[] { tenantId })).GetValueOrDefault(tenantId, true);

    public async Task<Dictionary<int, bool>> GetExpiredStatesAsync(IEnumerable<int> ids)
    {
        var tenantIds = ids.Where(x => x > 0).Distinct().ToArray();
        if (tenantIds.Length == 0) return new();
        var today = Today;
        var tenants = await db.ClientesApp.AsNoTracking().Where(x => tenantIds.Contains(x.Id)).ToListAsync();
        var invoices = await db.SaasInvoices.AsNoTracking().Include(x => x.Payments)
            .Where(x => tenantIds.Contains(x.ClienteAppId) && x.VoidedAt == null).ToListAsync();
        var subscriptions = await db.SaasSubscriptions.AsNoTracking().Where(x => tenantIds.Contains(x.ClienteAppId))
            .ToDictionaryAsync(x => x.ClienteAppId);
        var invoicesByTenant = invoices.ToLookup(x => x.ClienteAppId);
        return tenants.ToDictionary(x => x.Id, x => IsExpired(x, subscriptions.GetValueOrDefault(x.Id), invoicesByTenant[x.Id].ToList(), today));
    }

    private static bool IsExpired(ClienteApp tenant, SaasSubscription? subscription, List<SaasInvoice> invoices, DateTime today)
    {
        if (invoices.Any(x => x.DueDate < today && Balance(x) > 0)) return true;
        if (subscription == null) return tenant.FechaInicioPlan?.Date > today || tenant.FechaFinPlan?.Date < today;
        var paid = invoices.Where(x => Balance(x) == 0).ToList();
        if (subscription.BaselineEnd == null && paid.Count == 0)
            return subscription.BaselineStart?.Date > today; // Cuenta histórica sin términos: no inventar una fecha de corte.
        return !CoverageIntervals(subscription, paid).Any(x => x.Start <= today && x.End >= today);
    }
    public static decimal Paid(SaasInvoice invoice) => invoice.Payments.Where(p => p.VoidedAt == null).Sum(p => p.Amount);
    public static decimal Balance(SaasInvoice invoice) => Math.Max(0, invoice.Amount - Paid(invoice));
    public static string Status(SaasInvoice invoice, DateTime localToday) => invoice.VoidedAt != null ? "Anulada"
        : Balance(invoice) == 0 ? "Pagada" : invoice.DueDate.Date < localToday.Date ? "Vencida"
        : Paid(invoice) > 0 ? "Parcial" : "Pendiente";

    public async Task<int> IssueInvoiceAsync(int tenantId, DateTime periodStart, DateTime periodEnd, DateTime dueDate,
        decimal amount, string concept, string actor, string key)
    {
        CheckActor(actor); CheckKey(key); Money(amount); concept = Text(concept, 200, "Concepto");
        periodStart = periodStart.Date; periodEnd = periodEnd.Date; dueDate = dueDate.Date;
        if (periodStart.Year is < 2000 or > 2100 || periodEnd.Year is < 2000 or > 2100 || dueDate.Year is < 2000 or > 2100)
            throw new InvalidOperationException("Las fechas de facturación deben estar entre 2000 y 2100.");
        if (periodEnd < periodStart || periodEnd > periodStart.AddYears(1).AddDays(-1))
            throw new InvalidOperationException("El período debe ser válido y no superar un año.");
        if (dueDate < periodStart.AddYears(-1) || dueDate > periodEnd.AddYears(1))
            throw new InvalidOperationException("La fecha de vencimiento no corresponde al período.");
        await using var tx = await quotas.BeginTenantMutationAsync(tenantId, false);
        var prior = await db.SaasInvoices.SingleOrDefaultAsync(x => x.ClienteAppId == tenantId && x.IdempotencyKey == key);
        if (prior != null)
        {
            if (prior.PeriodStart != periodStart || prior.PeriodEnd != periodEnd || prior.DueDate != dueDate || prior.Amount != amount || prior.Concept != concept || prior.CreatedBy != actor)
                throw Conflict();
            await tx.CommitAsync(); return prior.Id;
        }
        if (await db.SaasInvoices.AnyAsync(x => x.ClienteAppId == tenantId && x.VoidedAt == null && x.PeriodStart <= periodEnd && x.PeriodEnd >= periodStart))
            throw new InvalidOperationException("Ya existe una factura vigente que cubre parte de ese período.");
        var tenant = await db.ClientesApp.SingleAsync(x => x.Id == tenantId);
        var subscription = await db.SaasSubscriptions.SingleOrDefaultAsync(x => x.ClienteAppId == tenantId);
        if (subscription == null)
            db.SaasSubscriptions.Add(new SaasSubscription { ClienteAppId = tenantId, BaselineStart = tenant.FechaInicioPlan?.Date,
                BaselineEnd = tenant.FechaFinPlan?.Date, BaselineTrial = tenant.EsTrial == true, CreatedAt = Now });
        var invoice = new SaasInvoice { ClienteAppId = tenantId, PeriodStart = periodStart, PeriodEnd = periodEnd,
            DueDate = dueDate, Amount = amount, Concept = concept, CreatedAt = Now, CreatedBy = actor, IdempotencyKey = key };
        db.SaasInvoices.Add(invoice);
        await db.SaveChangesAsync();
        Audit(tenantId, "FacturaEmitida", "Factura", invoice.Id, $"DOP {amount:0.00}; {periodStart:yyyy-MM-dd} a {periodEnd:yyyy-MM-dd}; vence {dueDate:yyyy-MM-dd}", actor, key);
        tenant.ProximoCobro = await NextDueAsync(tenantId, tenant.FechaFinPlan);
        tenant.UpdatedAt = Now; tenant.UpdatedBy = actor;
        await db.SaveChangesAsync(); await tx.CommitAsync(); return invoice.Id;
    }

    public async Task<int> RecordPaymentAsync(int tenantId, int invoiceId, decimal amount, DateTime paidOn,
        string method, string reference, string actor, string key)
    {
        CheckActor(actor); CheckKey(key); Money(amount);
        method = Text(method, 30, "Método"); reference = Text(reference, 100, "Referencia"); paidOn = paidOn.Date;
        if (method is not ("Transferencia" or "Efectivo" or "Tarjeta" or "Otro")) throw new InvalidOperationException("Método de pago inválido.");
        if (paidOn > Today || paidOn < new DateTime(2000, 1, 1)) throw new InvalidOperationException("El pago debe tener una fecha real, nunca futura.");
        var referenceKey = $"{method.ToUpperInvariant()}:{reference.ToUpperInvariant()}";
        await using var tx = await quotas.BeginTenantMutationAsync(tenantId, false);
        var prior = await db.SaasPayments.SingleOrDefaultAsync(p => p.ClienteAppId == tenantId && p.IdempotencyKey == key);
        if (prior != null)
        {
            if (prior.InvoiceId != invoiceId || prior.Amount != amount || prior.PaidOn != paidOn || prior.Method != method || prior.Reference != reference || prior.CreatedBy != actor)
                throw Conflict();
            await tx.CommitAsync(); return prior.Id;
        }
        var invoice = await db.SaasInvoices.Include(x => x.Payments).SingleOrDefaultAsync(x => x.ClienteAppId == tenantId && x.Id == invoiceId)
            ?? throw new InvalidOperationException("Factura no disponible para esta empresa.");
        if (invoice.VoidedAt != null) throw new InvalidOperationException("No se puede pagar una factura anulada.");
        if (amount > Balance(invoice)) throw new InvalidOperationException("El pago excede el saldo de la factura.");
        if (await db.SaasPayments.AnyAsync(x => x.ClienteAppId == tenantId && x.ReferenceKey == referenceKey))
            throw new InvalidOperationException("Esta referencia de pago ya fue registrada para la empresa.");
        var payment = new SaasPayment { ClienteAppId = tenantId, InvoiceId = invoiceId, Amount = amount, PaidOn = paidOn,
            Method = method, Reference = reference, ReferenceKey = referenceKey, CreatedAt = Now, CreatedBy = actor, IdempotencyKey = key };
        db.SaasPayments.Add(payment); await db.SaveChangesAsync();
        await RecomputeCoverageAsync(tenantId, actor);
        Audit(tenantId, "PagoConfirmado", "Pago", payment.Id, $"Factura {invoiceId}; DOP {amount:0.00}; {method}; referencia {reference}; fecha {paidOn:yyyy-MM-dd}", actor, key);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return payment.Id;
    }

    public async Task VoidPaymentAsync(int tenantId, int paymentId, string reason, string actor, string key)
    {
        CheckActor(actor); CheckKey(key); reason = Text(reason, 500, "Motivo");
        await using var tx = await quotas.BeginTenantMutationAsync(tenantId, false);
        if (await RepeatedAuditAsync(tenantId, "PagoAnulado", "Pago", paymentId, reason, actor, key)) { await tx.CommitAsync(); return; }
        var payment = await db.SaasPayments.SingleOrDefaultAsync(x => x.ClienteAppId == tenantId && x.Id == paymentId)
            ?? throw new InvalidOperationException("Pago no disponible para esta empresa.");
        if (payment.VoidedAt != null) throw new InvalidOperationException("El pago ya está anulado.");
        payment.VoidedAt = Now; payment.VoidReason = reason;
        await db.SaveChangesAsync(); await RecomputeCoverageAsync(tenantId, actor);
        Audit(tenantId, "PagoAnulado", "Pago", paymentId, reason, actor, key);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task VoidInvoiceAsync(int tenantId, int invoiceId, string reason, string actor, string key)
    {
        CheckActor(actor); CheckKey(key); reason = Text(reason, 500, "Motivo");
        await using var tx = await quotas.BeginTenantMutationAsync(tenantId, false);
        if (await RepeatedAuditAsync(tenantId, "FacturaAnulada", "Factura", invoiceId, reason, actor, key)) { await tx.CommitAsync(); return; }
        var invoice = await db.SaasInvoices.Include(x => x.Payments).SingleOrDefaultAsync(x => x.ClienteAppId == tenantId && x.Id == invoiceId)
            ?? throw new InvalidOperationException("Factura no disponible para esta empresa.");
        if (invoice.VoidedAt != null) throw new InvalidOperationException("La factura ya está anulada.");
        if (Paid(invoice) != 0) throw new InvalidOperationException("Anula primero los pagos de esta factura con sus motivos correspondientes.");
        invoice.VoidedAt = Now; invoice.VoidReason = reason;
        await db.SaveChangesAsync(); await RecomputeCoverageAsync(tenantId, actor);
        Audit(tenantId, "FacturaAnulada", "Factura", invoiceId, reason, actor, key);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task ChangePlanAsync(int tenantId, int planId, bool resetOverrides, string reason, string actor, string key)
    {
        CheckActor(actor); CheckKey(key); reason = Text(reason, 500, "Motivo");
        var detail = $"Plan {planId}; restablecer límites: {resetOverrides}; {reason}";
        await using var tx = await quotas.BeginTenantMutationAsync(tenantId, false);
        if (await RepeatedAuditAsync(tenantId, "PlanCambiado", "Empresa", tenantId, detail, actor, key)) { await tx.CommitAsync(); return; }
        var tenant = await db.ClientesApp.SingleAsync(x => x.Id == tenantId);
        var before = $"Plan {tenant.PlanId}; límites piloto {tenant.MaxPilotos?.ToString() ?? "del plan"}; órdenes {tenant.MaxOrdenesMes?.ToString() ?? "del plan"}; usuarios {tenant.MaxUsuarios?.ToString() ?? "sin límite"}";
        await ApplyPlanAsync(tenant, planId, resetOverrides, actor);
        Audit(tenantId, "PlanCambiado", "Empresa", tenantId, detail, actor, key);
        var entry = db.ChangeTracker.Entries<SaasAuditLog>().Single(x => x.State == EntityState.Added && x.Entity.OperationKey == key).Entity;
        entry.Before = before; entry.After = $"Plan {planId}; restablecer límites: {resetOverrides}";
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task<int> RequestPlanChangeAsync(int tenantId, int planId, string reason, string actor, string key)
    {
        CheckActor(actor); CheckKey(key); reason = Text(reason, 500, "Motivo");
        await using var tx = await quotas.BeginTenantMutationAsync(tenantId, false);
        var tenant = await db.ClientesApp.SingleAsync(x => x.Id == tenantId);
        // La pertenencia se valida de nuevo aun si el controlador ya resolvió la empresa.
        if (tenant.GestorIdentityUserId != actor) throw new UnauthorizedAccessException("Sólo el gestor de esta empresa puede solicitar su cambio de plan.");
        var prior = await db.SaasPlanRequests.SingleOrDefaultAsync(x => x.ClienteAppId == tenantId && x.IdempotencyKey == key);
        if (prior != null)
        {
            if (prior.RequestedPlanId != planId || prior.Reason != reason || prior.RequestedBy != actor) throw Conflict();
            await tx.CommitAsync(); return prior.Id;
        }
        if (tenant.PlanId == planId) throw new InvalidOperationException("La empresa ya tiene este plan.");
        if (!await db.Planes.AnyAsync(x => x.Id == planId && x.Activo)) throw new InvalidOperationException("El plan solicitado no está disponible.");
        if (await db.SaasPlanRequests.AnyAsync(x => x.ClienteAppId == tenantId && x.Status == "Pendiente"))
            throw new InvalidOperationException("Ya existe una solicitud pendiente para esta empresa.");
        var request = new SaasPlanRequest { ClienteAppId = tenantId, PreviousPlanId = tenant.PlanId, RequestedPlanId = planId,
            Reason = reason, RequestedBy = actor, CreatedAt = Now, IdempotencyKey = key };
        db.SaasPlanRequests.Add(request); await db.SaveChangesAsync();
        Audit(tenantId, "CambioPlanSolicitado", "Solicitud", request.Id, $"Plan {planId}; {reason}", actor, key);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return request.Id;
    }

    public async Task ResolvePlanRequestAsync(int requestId, bool approve, bool resetOverrides, string resolution, string actor, string key)
    {
        CheckActor(actor); CheckKey(key); resolution = Text(resolution, 500, "Resolución");
        var tenantId = await db.SaasPlanRequests.AsNoTracking().Where(x => x.Id == requestId).Select(x => (int?)x.ClienteAppId).SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Solicitud no disponible.");
        await using var tx = await quotas.BeginTenantMutationAsync(tenantId, false);
        var request = await db.SaasPlanRequests.SingleAsync(x => x.Id == requestId && x.ClienteAppId == tenantId);
        var result = approve ? "Aprobada" : "Rechazada";
        if (request.ResolutionKey == key)
        {
            if (request.Status != result || request.Resolution != resolution || request.ResolvedBy != actor || request.ResolutionResetOverrides != resetOverrides) throw Conflict();
            await tx.CommitAsync(); return;
        }
        if (request.Status != "Pendiente") throw new InvalidOperationException("La solicitud ya fue resuelta.");
        var tenant = await db.ClientesApp.SingleAsync(x => x.Id == tenantId);
        if (approve)
        {
            if (tenant.PlanId != request.PreviousPlanId) throw new InvalidOperationException("El plan de la empresa cambió; rechaza esta solicitud y pide una nueva.");
            await ApplyPlanAsync(tenant, request.RequestedPlanId, resetOverrides, actor);
        }
        request.Status = result; request.ResolvedAt = Now; request.ResolvedBy = actor; request.Resolution = resolution; request.ResolutionKey = key; request.ResolutionResetOverrides = resetOverrides;
        Audit(tenantId, $"Solicitud{result}", "Solicitud", requestId, $"Restablecer límites: {resetOverrides}; {resolution}", actor, key);
        if (approve)
        {
            var entry = db.ChangeTracker.Entries<SaasAuditLog>().Single(x => x.State == EntityState.Added && x.Entity.OperationKey == key).Entity;
            entry.Before = $"Plan {request.PreviousPlanId}"; entry.After = $"Plan {request.RequestedPlanId}";
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    private async Task ApplyPlanAsync(ClienteApp tenant, int planId, bool resetOverrides, string actor)
    {
        var plan = await db.Planes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId && x.Activo)
            ?? throw new InvalidOperationException("El plan no está disponible.");
        var maxPilots = resetOverrides ? plan.MaxPilotos : tenant.MaxPilotos ?? plan.MaxPilotos;
        var maxOrders = resetOverrides ? plan.MaxOrdenesPorMes : tenant.MaxOrdenesMes ?? plan.MaxOrdenesPorMes;
        var localMonth = new DateTime(Today.Year, Today.Month, 1);
        var from = DateTime.SpecifyKind(localMonth.AddHours(4), DateTimeKind.Utc); var until = from.AddMonths(1);
        CheckLimit(plan.MaxTiendas, await db.Tiendas.CountAsync(x => x.ClienteAppId == tenant.Id && x.Activo), "comercios");
        CheckLimit(maxPilots, await db.Pilotos.CountAsync(x => x.ClienteAppId == tenant.Id && x.Activo), "pilotos");
        CheckLimit(maxOrders, await db.OrdenesEntrega.CountAsync(x => x.ClienteAppId == tenant.Id && x.CreatedAt >= from && x.CreatedAt < until), "órdenes del mes");
        if (resetOverrides) { tenant.MaxPilotos = null; tenant.MaxOrdenesMes = null; tenant.MaxUsuarios = null; }
        tenant.PlanId = plan.Id; tenant.UpdatedAt = Now; tenant.UpdatedBy = actor;
    }

    // Recalcula desde los períodos reales. Los huecos sin facturar no se cobran ni se habilitan retroactivamente.
    private async Task RecomputeCoverageAsync(int tenantId, string actor)
    {
        var tenant = await db.ClientesApp.SingleAsync(x => x.Id == tenantId);
        var subscription = await db.SaasSubscriptions.SingleAsync(x => x.ClienteAppId == tenantId);
        var invoices = await db.SaasInvoices.Include(x => x.Payments).Where(x => x.ClienteAppId == tenantId && x.VoidedAt == null)
            .OrderBy(x => x.PeriodStart).ToListAsync();
        var paid = invoices.Where(x => Balance(x) == 0).ToList();
        var intervals = CoverageIntervals(subscription, paid);
        var selected = intervals.FirstOrDefault(x => x.Start <= Today && x.End >= Today);
        if (selected == default) selected = intervals.FirstOrDefault(x => x.Start > Today);
        if (selected == default && intervals.Count != 0) selected = intervals[^1];
        DateTime? coverage = selected == default ? subscription.BaselineEnd : selected.End;
        DateTime? start = selected == default ? subscription.BaselineStart : selected.Start == DateTime.MinValue ? null : selected.Start;
        tenant.FechaInicioPlan = start;
        tenant.FechaFinPlan = coverage;
        tenant.EsTrial = subscription.BaselineTrial && !paid.Any(x => x.PeriodStart <= Today);
        tenant.UltimoPago = invoices.SelectMany(x => x.Payments).Where(x => x.VoidedAt == null).Select(x => (DateTime?)x.PaidOn).Max();
        tenant.ProximoCobro = invoices.Where(x => Balance(x) > 0).Select(x => (DateTime?)x.DueDate).Min() ?? coverage?.AddDays(1);
        tenant.UpdatedAt = Now; tenant.UpdatedBy = actor;
    }

    private static List<(DateTime Start, DateTime End)> CoverageIntervals(SaasSubscription subscription, List<SaasInvoice> paid)
    {
        var source = paid.Select(x => (Start: x.PeriodStart, End: x.PeriodEnd)).ToList();
        if (subscription.BaselineEnd != null)
            source.Add((subscription.BaselineStart ?? DateTime.MinValue, subscription.BaselineEnd.Value));
        var result = new List<(DateTime Start, DateTime End)>();
        foreach (var interval in source.OrderBy(x => x.Start))
        {
            if (result.Count == 0 || (interval.Start - result[^1].End).TotalDays > 1) result.Add(interval);
            else if (interval.End > result[^1].End) result[^1] = (result[^1].Start, interval.End);
        }
        return result;
    }

    private async Task<DateTime?> NextDueAsync(int tenantId, DateTime? coverage)
    {
        var invoices = await db.SaasInvoices.Include(x => x.Payments).Where(x => x.ClienteAppId == tenantId && x.VoidedAt == null).ToListAsync();
        return invoices.Where(x => Balance(x) > 0).Select(x => (DateTime?)x.DueDate).Min() ?? coverage?.AddDays(1);
    }

    public async Task AuditAsync(int? tenantId, string action, string entity, string entityId, string detail, string actor)
    {
        CheckActor(actor);
        db.SaasAuditLogs.Add(new SaasAuditLog { ClienteAppId = tenantId, Action = Text(action, 80, "Acción"), Entity = Text(entity, 60, "Entidad"),
            EntityId = Text(entityId, 100, "Identificador"), Detail = Text(detail, 2000, "Detalle"), Actor = actor, CreatedAt = Now });
        await db.SaveChangesAsync();
    }

    private void Audit(int tenantId, string action, string entity, int id, string detail, string actor, string key)
        => db.SaasAuditLogs.Add(new SaasAuditLog { ClienteAppId = tenantId, Action = action, Entity = entity, EntityId = id.ToString(),
            Detail = detail, Actor = actor, CreatedAt = Now, OperationKey = key });
    private async Task<bool> RepeatedAuditAsync(int tenantId, string action, string entity, int id, string detail, string actor, string key)
    {
        var prior = await db.SaasAuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.ClienteAppId == tenantId && x.Action == action && x.OperationKey == key);
        if (prior == null) return false;
        if (prior.Entity != entity || prior.EntityId != id.ToString() || prior.Detail != detail || prior.Actor != actor) throw Conflict();
        return true;
    }
    private static void CheckLimit(int max, int used, string kind)
    {
        if (max < 0 || (max > 0 && used > max)) throw new InvalidOperationException($"El plan no admite el uso actual de {kind} ({used}).");
    }
    private static string Text(string? value, int max, string label)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > max) throw new InvalidOperationException($"{label}: dato requerido de hasta {max} caracteres.");
        return value;
    }
    private static void Money(decimal amount)
    {
        if (amount <= 0 || amount > 999999999.99m || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("El importe debe ser positivo y tener hasta dos decimales.");
    }
    private static void CheckActor(string actor) => Text(actor, 150, "Usuario");
    private static void CheckKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 80 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidOperationException("Identificador de operación inválido.");
    }
    private static InvalidOperationException Conflict() => new("El identificador de operación ya fue usado con otros datos.");
}
