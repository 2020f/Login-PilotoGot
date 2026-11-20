using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Saas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class SaasBillingTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private DateTime Today => app.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-4)).Date;
    private static string Key() => Guid.NewGuid().ToString("N");
    private Task Scope(Func<ApplicationDbContext, SaasBillingService, Task> action) => app.InScopeAsync(sp =>
        action(sp.GetRequiredService<ApplicationDbContext>(), new SaasBillingService(sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<QuotaPolicyService>(), app.Clock)));
    private Task<int> Invoice(SaasBillingService service, int tenant, decimal amount = 100m, DateTime? due = null)
        => service.IssueInvoiceAsync(tenant, Today, Today.AddMonths(1).AddDays(-1), due ?? Today, amount, "Mensualidad QA", "qa-admin", Key());

    [Fact]
    public Task Invoice_dates_never_mark_payment_and_due_day_is_inclusive() => Scope(async (db, service) =>
    {
        var id = await Invoice(service, app.TenantA);
        var invoice = await db.SaasInvoices.Include(x => x.Payments).SingleAsync(x => x.Id == id);
        Assert.Equal("Pendiente", SaasBillingService.Status(invoice, Today));
        Assert.Equal(100m, SaasBillingService.Balance(invoice));
        Assert.Null((await db.ClientesApp.FindAsync(app.TenantA))!.UltimoPago);
        Assert.False(await service.IsExpiredAsync(app.TenantA));
        app.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True(await service.IsExpiredAsync(app.TenantA));
        Assert.Equal("Vencida", SaasBillingService.Status(invoice, Today));
    });

    [Fact]
    public Task Partial_payment_does_not_renew_full_payment_renews_but_keeps_manual_suspension() => Scope(async (db, service) =>
    {
        var tenant = (await db.ClientesApp.FindAsync(app.TenantA))!;
        tenant.Estado = EstadoClienteApp.Suspendido; await db.SaveChangesAsync();
        var id = await Invoice(service, app.TenantA);
        await service.RecordPaymentAsync(app.TenantA, id, 40m, Today, "Transferencia", "REF-40", "qa-admin", Key());
        Assert.Null(tenant.FechaFinPlan);
        var invoice = await db.SaasInvoices.Include(x => x.Payments).SingleAsync(x => x.Id == id);
        Assert.Equal("Parcial", SaasBillingService.Status(invoice, Today));
        await service.RecordPaymentAsync(app.TenantA, id, 60m, Today, "Transferencia", "REF-60", "qa-admin", Key());
        Assert.Equal("Pagada", SaasBillingService.Status(invoice, Today));
        Assert.Equal(invoice.PeriodEnd, tenant.FechaFinPlan);
        Assert.Equal(invoice.PeriodEnd.AddDays(1), tenant.ProximoCobro);
        Assert.Equal(EstadoClienteApp.Suspendido, tenant.Estado);
        Assert.Equal(2, await db.SaasAuditLogs.CountAsync(x => x.Action == "PagoConfirmado"));
    });

    [Fact]
    public Task Payments_are_idempotent_and_conflicting_replays_cannot_change_amounts() => Scope(async (db, service) =>
    {
        var key = Key(); var invoiceKey = Key();
        var id = await service.IssueInvoiceAsync(app.TenantA, Today, Today.AddDays(29), Today, 100m, "QA", "qa-admin", invoiceKey);
        Assert.Equal(id, await service.IssueInvoiceAsync(app.TenantA, Today, Today.AddDays(29), Today, 100m, "QA", "qa-admin", invoiceKey));
        var payment = await service.RecordPaymentAsync(app.TenantA, id, 40m, Today, "Transferencia", "REF-01", "qa-admin", key);
        Assert.Equal(payment, await service.RecordPaymentAsync(app.TenantA, id, 40m, Today, "Transferencia", "REF-01", "qa-admin", key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordPaymentAsync(app.TenantA, id, 60m, Today, "Transferencia", "REF-01", "qa-admin", key));
        Assert.Single(await db.SaasPayments.ToListAsync());
        Assert.Equal(40m, await db.SaasPayments.SumAsync(x => x.Amount));
    });

    [Fact]
    public Task Duplicate_bank_reference_is_rejected_even_with_new_operation_key() => Scope(async (db, service) =>
    {
        var id = await Invoice(service, app.TenantA);
        await service.RecordPaymentAsync(app.TenantA, id, 40m, Today, "Transferencia", "ABC-1", "qa-admin", Key());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordPaymentAsync(app.TenantA, id, 40m, Today, "Transferencia", "abc-1", "qa-admin", Key()));
        var b = await Invoice(service, app.TenantB);
        await service.RecordPaymentAsync(app.TenantB, b, 40m, Today, "Transferencia", "abc-1", "qa-admin", Key());
        Assert.Equal(2, await db.SaasPayments.CountAsync());
    });

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10.001")]
    [InlineData("1000000000")]
    public Task Invalid_amount_never_creates_an_invoice(string raw) => Scope(async (db, service) =>
    {
        var amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoice(service, app.TenantA, amount));
        Assert.Empty(await db.SaasInvoices.ToListAsync());
    });

    [Theory]
    [InlineData(1)]
    [InlineData(9999)]
    public Task Malformed_extreme_invoice_dates_are_domain_errors(int year) => Scope(async (db, service) =>
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueInvoiceAsync(app.TenantA,
            new DateTime(year, 1, 1), new DateTime(year, 1, 31), new DateTime(year, 1, 1), 100m, "QA", "qa-admin", Key()));
        Assert.Empty(await db.SaasInvoices.ToListAsync());
    });

    [Fact]
    public Task Future_payment_overpayment_and_cross_tenant_payment_are_rejected() => Scope(async (db, service) =>
    {
        var id = await Invoice(service, app.TenantA);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordPaymentAsync(app.TenantA, id, 100m, Today.AddDays(1), "Efectivo", "FUTURE", "qa-admin", Key()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordPaymentAsync(app.TenantA, id, 101m, Today, "Efectivo", "OVER", "qa-admin", Key()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordPaymentAsync(app.TenantB, id, 100m, Today, "Efectivo", "B", "qa-admin", Key()));
        Assert.Empty(await db.SaasPayments.ToListAsync());
    });

    [Fact]
    public Task Voiding_receipt_reverts_coverage_and_keeps_immutable_audit() => Scope(async (db, service) =>
    {
        var tenant = (await db.ClientesApp.FindAsync(app.TenantA))!;
        tenant.FechaInicioPlan = Today.AddDays(-30); tenant.FechaFinPlan = Today.AddDays(-1); await db.SaveChangesAsync();
        var id = await Invoice(service, app.TenantA);
        var payment = await service.RecordPaymentAsync(app.TenantA, id, 100m, Today, "Transferencia", "VOID-1", "qa-admin", Key());
        Assert.False(await service.IsExpiredAsync(app.TenantA));
        var key = Key();
        await service.VoidPaymentAsync(app.TenantA, payment, "Recibo duplicado", "qa-admin", key);
        await service.VoidPaymentAsync(app.TenantA, payment, "Recibo duplicado", "qa-admin", key);
        Assert.True(await service.IsExpiredAsync(app.TenantA));
        Assert.Equal(Today.AddDays(-1), tenant.FechaFinPlan);
        Assert.NotNull((await db.SaasPayments.FindAsync(payment))!.VoidedAt);
        Assert.Single(await db.SaasAuditLogs.Where(x => x.Action == "PagoAnulado").ToListAsync());
        var audit = await db.SaasAuditLogs.FirstAsync(); audit.Detail = "Sobrescrito";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    });

    [Fact]
    public Task Paid_invoice_cannot_be_voided_until_receipts_are_voided() => Scope(async (db, service) =>
    {
        var id = await Invoice(service, app.TenantA);
        var payment = await service.RecordPaymentAsync(app.TenantA, id, 100m, Today, "Efectivo", "CASH-1", "qa-admin", Key());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VoidInvoiceAsync(app.TenantA, id, "Error", "qa-admin", Key()));
        await service.VoidPaymentAsync(app.TenantA, payment, "Recibo incorrecto", "qa-admin", Key());
        await service.VoidInvoiceAsync(app.TenantA, id, "Periodo incorrecto", "qa-admin", Key());
        var invoice = await db.SaasInvoices.Include(x => x.Payments).SingleAsync(x => x.Id == id);
        Assert.Equal("Anulada", SaasBillingService.Status(invoice, Today));
        Assert.Single(invoice.Payments);
    });

    [Fact]
    public Task Overlapping_period_is_rejected_and_voided_invoice_can_be_reissued() => Scope(async (db, service) =>
    {
        var id = await Invoice(service, app.TenantA);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoice(service, app.TenantA));
        await service.VoidInvoiceAsync(app.TenantA, id, "Importe incorrecto", "qa-admin", Key());
        Assert.NotEqual(id, await Invoice(service, app.TenantA, 120m));
        Assert.Equal(2, await db.SaasInvoices.CountAsync());
    });

    [Fact]
    public Task Plan_upgrade_preserves_orders_and_overrides_and_downgrade_below_use_fails() => Scope(async (db, service) =>
    {
        var unlimited = new Plan { Nombre = "Amplio", PrecioMensual = 200, MaxTiendas = 0, MaxPilotos = 0, MaxOrdenesPorMes = 0 };
        var small = new Plan { Nombre = "Pequeño", PrecioMensual = 50, MaxTiendas = 1, MaxPilotos = 1, MaxOrdenesPorMes = 1 };
        db.Planes.AddRange(unlimited, small); await db.SaveChangesAsync();
        var tenant = (await db.ClientesApp.FindAsync(app.TenantA))!; tenant.MaxPilotos = 7; await db.SaveChangesAsync();
        var count = await db.OrdenesEntrega.CountAsync();
        await service.ChangePlanAsync(app.TenantA, unlimited.Id, false, "Ampliación", "qa-admin", Key());
        Assert.Equal(unlimited.Id, tenant.PlanId); Assert.Equal(7, tenant.MaxPilotos);
        Assert.Equal(count, await db.OrdenesEntrega.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangePlanAsync(app.TenantA, small.Id, true, "Reducción", "qa-admin", Key()));
        Assert.Equal(unlimited.Id, tenant.PlanId);
        await service.ChangePlanAsync(app.TenantA, unlimited.Id, true, "Usar límites del plan", "qa-admin", Key());
        Assert.Null(tenant.MaxPilotos);
        Assert.Equal(0, (await new QuotaPolicyService(db).ResolveAsync(app.TenantA)).MaxPilotos);
        Assert.Equal(2, await db.SaasAuditLogs.CountAsync(x => x.Action == "PlanCambiado"));
    });

    [Fact]
    public Task Request_belongs_to_own_manager_and_approval_is_unique_and_audited() => Scope(async (db, service) =>
    {
        var plan = new Plan { Nombre = "Premium", MaxTiendas = 0, MaxPilotos = 0, MaxOrdenesPorMes = 0 };
        db.Planes.Add(plan); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestPlanChangeAsync(app.TenantB, plan.Id, "Quiero ampliar", "manager-a", Key()));
        var request = await service.RequestPlanChangeAsync(app.TenantA, plan.Id, "Quiero ampliar", "manager-a", Key());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestPlanChangeAsync(app.TenantA, plan.Id, "Segundo intento", "manager-a", Key()));
        var key = Key();
        await service.ResolvePlanRequestAsync(request, true, true, "Aprobado por soporte", "qa-admin", key);
        await service.ResolvePlanRequestAsync(request, true, true, "Aprobado por soporte", "qa-admin", key);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolvePlanRequestAsync(request, false, true, "Cambio", "qa-admin", Key()));
        Assert.Equal(plan.Id, (await db.ClientesApp.FindAsync(app.TenantA))!.PlanId);
        Assert.Equal("Aprobada", (await db.SaasPlanRequests.FindAsync(request))!.Status);
        Assert.Single(await db.SaasAuditLogs.Where(x => x.Action == "SolicitudAprobada").ToListAsync());
    });

    [Fact]
    public Task Legacy_without_dates_is_allowed_trial_expiration_and_future_start_are_real() => Scope(async (db, service) =>
    {
        Assert.False(await service.IsExpiredAsync(app.TenantA));
        var tenant = (await db.ClientesApp.FindAsync(app.TenantA))!;
        tenant.EsTrial = true; tenant.FechaFinPlan = Today; await db.SaveChangesAsync();
        Assert.False(await service.IsExpiredAsync(app.TenantA));
        tenant.FechaFinPlan = Today.AddDays(-1); await db.SaveChangesAsync();
        Assert.True(await service.IsExpiredAsync(app.TenantA));
        tenant.FechaFinPlan = Today.AddMonths(1); tenant.FechaInicioPlan = Today.AddDays(1); await db.SaveChangesAsync();
        Assert.True(await service.IsExpiredAsync(app.TenantA));
        Assert.False(await service.IsExpiredAsync(app.TenantB));
    });

    [Fact]
    public Task Concurrent_receipt_retry_is_one_payment_and_one_audit() => Scope(async (db, service) =>
    {
        if (!db.Database.IsSqlServer()) return;
        var invoice = await Invoice(service, app.TenantA);
        var key = Key(); var ids = new int[2];
        await Task.WhenAll(Enumerable.Range(0, 2).Select(i => app.InScopeAsync(async sp =>
        {
            var scopedDb = sp.GetRequiredService<ApplicationDbContext>();
            var scopedService = new SaasBillingService(scopedDb, sp.GetRequiredService<QuotaPolicyService>(), app.Clock);
            ids[i] = await scopedService.RecordPaymentAsync(app.TenantA, invoice, 100m, Today, "Transferencia", "RACE-1", "qa-admin", key);
        })));
        Assert.Equal(ids[0], ids[1]);
        Assert.Single(await db.SaasPayments.Where(x => x.InvoiceId == invoice).ToListAsync());
        Assert.Single(await db.SaasAuditLogs.Where(x => x.Action == "PagoConfirmado").ToListAsync());
    });

    [Fact]
    public Task Database_foreign_key_prevents_payment_cross_tenant_even_without_service() => Scope(async (db, service) =>
    {
        var invoice = await Invoice(service, app.TenantA);
        db.SaasPayments.Add(new SaasPayment { ClienteAppId = app.TenantB, InvoiceId = invoice, Amount = 100m,
            PaidOn = Today, Method = "Efectivo", Reference = "FORGED", ReferenceKey = "EFECTIVO:FORGED", CreatedAt = DateTime.UtcNow,
            CreatedBy = "qa-admin", IdempotencyKey = Key() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    });

    [Fact]
    public Task Renewal_after_unbilled_gap_reactivates_only_the_paid_period() => Scope(async (db, service) =>
    {
        var tenant = (await db.ClientesApp.FindAsync(app.TenantA))!;
        tenant.FechaInicioPlan = Today.AddMonths(-2); tenant.FechaFinPlan = Today.AddDays(-10); await db.SaveChangesAsync();
        var invoice = await Invoice(service, app.TenantA);
        Assert.True(await service.IsExpiredAsync(app.TenantA));
        await service.RecordPaymentAsync(app.TenantA, invoice, 100m, Today, "Transferencia", "RENEW-GAP", "qa-admin", Key());
        Assert.False(await service.IsExpiredAsync(app.TenantA));
        Assert.Equal(Today, tenant.FechaInicioPlan);
        Assert.Equal(Today.AddMonths(1).AddDays(-1), tenant.FechaFinPlan);
    });

    [Fact]
    public Task Paying_a_future_period_does_not_authorize_today_or_an_intermediate_gap() => Scope(async (db, service) =>
    {
        var tenant = (await db.ClientesApp.FindAsync(app.TenantA))!;
        tenant.FechaFinPlan = Today.AddDays(-10); await db.SaveChangesAsync();
        var next = Today.AddDays(10);
        var invoice = await service.IssueInvoiceAsync(app.TenantA, next, next.AddDays(29), Today, 100m, "Renovación futura", "qa-admin", Key());
        await service.RecordPaymentAsync(app.TenantA, invoice, 100m, Today, "Transferencia", "PREPAY-FUTURE", "qa-admin", Key());
        Assert.True(await service.IsExpiredAsync(app.TenantA));
        app.Clock.Advance(TimeSpan.FromDays(9));
        Assert.True(await service.IsExpiredAsync(app.TenantA));
        app.Clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await service.IsExpiredAsync(app.TenantA));
        app.Clock.Advance(TimeSpan.FromDays(30));
        Assert.True(await service.IsExpiredAsync(app.TenantA));
    });
}
