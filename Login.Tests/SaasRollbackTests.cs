using System.Text.Json;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Saas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class SaasRollbackTests
{
    private const string Latest = "20261112201846_EnvirapidoSaasBilling";
    private const string BeforeSaas = "20261112025055_EnvirapidoRetornosYCajaEmpresa";

    [SqlServerFact]
    public Task Downgrade_cannot_discard_invoice_payment_audit_or_subscription_coverage()
        => RefuseDowngrade(async (app, services) =>
        {
            var billing = services.GetRequiredService<SaasBillingService>();
            var invoice = await billing.IssueInvoiceAsync(app.TenantA, billing.Today, billing.Today.AddDays(29), billing.Today,
                100m, "Cobertura protegida QA", "qa-admin", Guid.NewGuid().ToString("N"));
            await billing.RecordPaymentAsync(app.TenantA, invoice, 100m, billing.Today, "Transferencia", "QA-preserved-payment",
                "qa-admin", Guid.NewGuid().ToString("N"));
            var db = services.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.SaasInvoices.ToListAsync());
            Assert.Single(await db.SaasPayments.ToListAsync());
            Assert.NotEmpty(await db.SaasAuditLogs.ToListAsync());
            Assert.Single(await db.SaasSubscriptions.ToListAsync());
            Assert.NotNull((await db.ClientesApp.AsNoTracking().SingleAsync(t => t.Id == app.TenantA)).FechaFinPlan);
        });

    [SqlServerFact]
    public Task Downgrade_cannot_discard_an_audit_only_ledger()
        => RefuseDowngrade(async (app, services) =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.SaasAuditLogs.Add(new SaasAuditLog
            {
                ClienteAppId = app.TenantA, Action = "EmpresaActualizada", Entity = "Empresa", EntityId = app.TenantA.ToString(),
                Actor = "qa-admin", Detail = "Cambio administrativo protegido", CreatedAt = app.Clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
            Assert.Empty(await db.SaasInvoices.ToListAsync());
            Assert.Single(await db.SaasAuditLogs.ToListAsync());
        });

    [SqlServerFact]
    public Task Downgrade_cannot_discard_preserved_subscription_baselines_without_invoices()
        => RefuseDowngrade(async (app, services) =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var today = app.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-4)).Date;
            db.SaasSubscriptions.Add(new SaasSubscription
            {
                ClienteAppId = app.TenantA, BaselineStart = today.AddDays(-10), BaselineEnd = today.AddDays(20),
                BaselineTrial = true, CreatedAt = app.Clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
            Assert.Empty(await db.SaasInvoices.ToListAsync());
            Assert.Empty(await db.SaasAuditLogs.ToListAsync());
            Assert.Single(await db.SaasSubscriptions.ToListAsync());
        });

    private static async Task RefuseDowngrade(Func<PilotGoFactory, IServiceProvider, Task> prepare)
    {
        using var app = new PilotGoFactory();
        await app.SeedAsync(Latest);
        await app.InScopeAsync(async services =>
        {
            await prepare(app, services);
            var db = services.GetRequiredService<ApplicationDbContext>();
            var historyBefore = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var dataBefore = await Snapshot(db);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(BeforeSaas));
            Assert.Contains("Rollback bloqueado", failure.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(historyBefore, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Contains(Latest, await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(dataBefore, await Snapshot(db));
        });
    }

    private static async Task<string> Snapshot(ApplicationDbContext db) => JsonSerializer.Serialize(new
    {
        Invoices = await db.SaasInvoices.AsNoTracking().OrderBy(i => i.Id)
            .Select(i => new { i.Id, i.ClienteAppId, i.Amount, i.PeriodStart, i.PeriodEnd, i.DueDate, i.VoidedAt, i.IdempotencyKey }).ToListAsync(),
        Payments = await db.SaasPayments.AsNoTracking().OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.ClienteAppId, p.InvoiceId, p.Amount, p.PaidOn, p.Reference, p.VoidedAt, p.IdempotencyKey }).ToListAsync(),
        Audit = await db.SaasAuditLogs.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.ClienteAppId, a.Action, a.Actor, a.Entity, a.EntityId, a.Detail, a.CreatedAt }).ToListAsync(),
        Baselines = await db.SaasSubscriptions.AsNoTracking().OrderBy(s => s.ClienteAppId)
            .Select(s => new { s.ClienteAppId, s.BaselineStart, s.BaselineEnd, s.BaselineTrial, s.CreatedAt }).ToListAsync(),
        Requests = await db.SaasPlanRequests.AsNoTracking().OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.ClienteAppId, r.RequestedPlanId, r.PreviousPlanId, r.Status, r.IdempotencyKey }).ToListAsync(),
        Tenants = await db.ClientesApp.AsNoTracking().OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.PlanId, t.Estado, t.FechaInicioPlan, t.FechaFinPlan, t.UltimoPago, t.ProximoCobro }).ToListAsync()
    });
}
