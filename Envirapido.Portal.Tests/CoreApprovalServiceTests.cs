using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Envirapido.Portal.Tests;

public sealed class CoreApprovalServiceTests
{
    [Fact]
    public async Task Approval_creates_one_immutable_decision_and_audit_entry()
    {
        using var app = new PortalFactory(); await app.InitializeAsync(); using var buyer = app.Buyer();
        var account = await app.RegisterAsync(buyer, "direct-approval@example.test", "plan-free");
        var request = Assert.Single(account.Requests);
        await app.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<PortalDomainService>();
            var approved = await service.DecideAsync(request.Id, new PortalDecisionInput("Approved", "Aprobación directa QA", request.Version), "qa-superadmin", Guid.NewGuid().ToString(), default);
            Assert.Equal("Approved", approved.Status);
        });
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PortalDbContext>();
            Assert.Equal(1, await db.Approvals.CountAsync());
            Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.Action == "RequestDecision"));
            Assert.Empty(await db.Payments.ToListAsync()); Assert.Empty(await db.AccessGrants.ToListAsync());
        });
    }
}
