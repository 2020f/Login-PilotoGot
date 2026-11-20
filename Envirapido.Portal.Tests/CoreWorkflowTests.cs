using System.Net;
using System.Net.Http.Json;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Envirapido.Portal.Tests;

public sealed class CoreWorkflowTests : IAsyncLifetime
{
    private readonly PortalFactory app = new();
    public Task InitializeAsync() => app.InitializeAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private static string Key() => Guid.NewGuid().ToString();
    private static Task<PortalAccountDto?> Account(HttpClient buyer) => buyer.GetFromJsonAsync<PortalAccountDto>("/portal-api/v1/account");
    private static async Task<PortalAdminRequestDetail> Detail(HttpClient admin, Guid id)
        => (await admin.GetFromJsonAsync<PortalAdminRequestDetail>("/portal-api/internal/admin/requests/" + id))!;
    private static PortalRegisterInput Input(PortalCatalogDto catalog, string email)
    {
        var plan = Assert.Single(catalog.Plans, p => p.Code == "plan-paid");
        return new(email, PortalFactory.Password, PortalFactory.Password, plan.Code, plan.Version,
            new("Responsable QA", "Empresa ficticia", "5550000123", "Distrito Nacional", "Santo Domingo", "Logística"), catalog.PolicyVersions.Terms, catalog.PolicyVersions.Privacy);
    }

    [Fact]
    public async Task Registration_replays_one_account_request_and_outbox_and_never_creates_CRM_entities()
    {
        using var buyer = app.Buyer();
        var catalog = (await buyer.GetFromJsonAsync<PortalCatalogDto>("/portal-api/v1/catalog"))!;
        var input = Input(catalog, "atomic@example.test"); var key = Key();
        using var unsigned = await buyer.PostAsJsonAsync("/portal-api/v1/auth/register", input);
        Assert.Equal(HttpStatusCode.BadRequest, unsigned.StatusCode);
        using var first = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var account = (await first.Content.ReadFromJsonAsync<PortalAccountDto>())!;
        Assert.False(account.User.EmailConfirmed); Assert.False(account.Access.Allowed); Assert.Null(account.Access.CrmUrl);
        var cookie = Assert.Single(first.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Envirapido.Portal=", StringComparison.Ordinal));
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        using var replay = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input, key);
        Assert.Equal(account.User.Id, (await replay.Content.ReadFromJsonAsync<PortalAccountDto>())!.User.Id);
        using var conflict = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input with { Profile = input.Profile with { BusinessName = "Otra carga" } }, key);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var badPlan = await PortalFactory.PostBuyerAsync(buyer, "auth/register", Input(catalog, "not-created@example.test") with { PlanVersion = "stale" });
        Assert.Equal(HttpStatusCode.Conflict, badPlan.StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PortalDbContext>();
            Assert.Equal(1, await db.Users.CountAsync()); Assert.Equal(1, await db.Profiles.CountAsync()); Assert.Equal(1, await db.Requests.CountAsync()); Assert.Equal(1, await db.Outbox.CountAsync());
            Assert.All(db.Model.GetEntityTypes(), entity =>
            {
                Assert.StartsWith("Envirapido.Portal.Api", entity.ClrType.Namespace);
                Assert.DoesNotContain(entity.GetProperties(), p => p.Name is "ClienteAppId" or "IdentityUserId" or "TiendaId" or "PilotoId");
            });
            Assert.All(await db.Tokens.ToListAsync(), token => Assert.Equal(64, token.Hash.Length));
            Assert.DoesNotContain("token=", (await db.Outbox.SingleAsync()).ProtectedMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Buyer_cookie_cannot_act_as_admin_and_requests_reports_are_owner_scoped()
    {
        using var first = app.Buyer(); using var second = app.Buyer();
        var a = await app.RegisterAsync(first, "scope-a@example.test"); var b = await app.RegisterAsync(second, "scope-b@example.test");
        var requestA = Assert.Single(a.Requests); var requestB = Assert.Single(b.Requests);
        using var deniedAdmin = await first.PostAsJsonAsync($"/portal-api/internal/admin/users/{a.User.Id}/status", new PortalAdminUserStatusInput("Suspended", "forged", "forged"));
        Assert.Equal(HttpStatusCode.Unauthorized, deniedAdmin.StatusCode);
        using var otherReport = await PortalFactory.PostBuyerAsync(first, $"requests/{requestB.Id}/payment-reports", new PortalPaymentReportInput(100, new(2026, 11, 17), "Transferencia", "QA-NOT-OWN"));
        Assert.Equal(HttpStatusCode.NotFound, otherReport.StatusCode);
        var own = (await first.GetFromJsonAsync<PortalRequestsDto>("/portal-api/v1/requests"))!;
        Assert.Equal(requestA.Id, Assert.Single(own.Requests).Id);
        using var report = await PortalFactory.PostBuyerAsync(first, $"requests/{requestA.Id}/payment-reports", new PortalPaymentReportInput(1500, new(2026, 11, 17), "Transferencia", "QA-REPORT-ONLY"));
        Assert.Equal(HttpStatusCode.Created, report.StatusCode);
        var after = Assert.Single((await Account(first))!.Requests);
        Assert.Equal("Reported", after.PaymentStatus); Assert.Equal(0, after.ConfirmedAmount); Assert.Equal(1500, after.AmountDue);
        Assert.False((await Account(first))!.Access.Allowed);
        using var anonymous = app.Buyer(); anonymous.DefaultRequestHeaders.Authorization = new("Bearer", "CRM-token-is-not-a-portal-session");
        using var unauthorized = await anonymous.GetAsync("/portal-api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode); Assert.Null(unauthorized.Headers.Location);
        Assert.Equal("application/json", unauthorized.Content.Headers.ContentType?.MediaType);
        await app.InScopeAsync(async sp => Assert.Empty(await sp.GetRequiredService<PortalDbContext>().Payments.ToListAsync()));
    }

    [Fact]
    public async Task Approval_payment_and_RD_window_are_independent_and_expiry_does_not_create_money()
    {
        using var buyer = app.Buyer(); using var admin = app.Admin();
        var account = await app.RegisterAsync(buyer, "window@example.test"); await app.ConfirmEmailAsync(buyer, account.User.Email);
        using var persistent = await PortalFactory.PostBuyerAsync(buyer, "auth/login", new PortalLoginInput(account.User.Email, PortalFactory.Password, true));
        persistent.EnsureSuccessStatusCode(); var id = Assert.Single(account.Requests).Id;
        var detail = await Detail(admin, id);
        using var approve = await PortalFactory.PostAdminAsync(admin, $"requests/{id}/decision", new PortalDecisionInput("Approved", "Aprobación QA independiente del dinero", detail.Request.Version));
        approve.EnsureSuccessStatusCode();
        using var grantResponse = await PortalFactory.PostAdminAsync(admin, "access", new PortalAccessInput(account.User.Id, id, new(2026, 11, 18), new(2026, 11, 19), "Enabled", "Ventana QA"));
        grantResponse.EnsureSuccessStatusCode();
        Assert.Equal("PAYMENT_PENDING", (await Account(buyer))!.Access.Reason);
        detail = await Detail(admin, id);
        using var payment = await PortalFactory.PostAdminAsync(admin, "payments/confirm", new PortalPaymentConfirmInput(id, 1500, new(2026, 11, 17), "Transferencia", "QA-WINDOW-RECEIPT", "Pago ficticio QA", detail.Request.Version));
        payment.EnsureSuccessStatusCode();
        Assert.Equal("NOT_STARTED", (await Account(buyer))!.Access.Reason);
        app.Clock.Set(new(2026, 11, 18, 4, 0, 0, TimeSpan.Zero));
        var active = (await Account(buyer))!.Access; Assert.True(active.Allowed); Assert.Equal("https://envirapido.com/Identity/Account/Login", active.CrmUrl);
        app.Clock.Set(new(2026, 11, 20, 3, 59, 59, TimeSpan.Zero)); Assert.True((await Account(buyer))!.Access.Allowed);
        app.Clock.Set(new(2026, 11, 20, 4, 0, 0, TimeSpan.Zero));
        var expired = (await Account(buyer))!; Assert.Equal("EXPIRED", expired.Access.Reason); Assert.Null(expired.Access.CrmUrl);
        await app.InScopeAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<PortalDbContext>().Payments.CountAsync()));
        using var extreme = await PortalFactory.PostAdminAsync(admin, "access", new PortalAccessInput(account.User.Id, id, new(2026, 11, 18), DateOnly.MaxValue, "Enabled", "Debe fallar", (await Detail(admin, id)).AccessGrant!.Version));
        Assert.Equal(HttpStatusCode.BadRequest, extreme.StatusCode);
    }

    [Fact]
    public async Task Decisions_and_confirmations_are_idempotent_concurrent_versions_cannot_overwrite_and_void_revokes_access()
    {
        using var buyer = app.Buyer(); using var admin = app.Admin(); var a = await app.RegisterAsync(buyer, "decision@example.test");
        await app.ConfirmEmailAsync(buyer, a.User.Email); var id = Assert.Single(a.Requests).Id;
        var detail = await Detail(admin, id); var input = new PortalDecisionInput("Approved", "Decisión QA", detail.Request.Version); var operation = Key();
        using var first = await PortalFactory.PostAdminAsync(admin, $"requests/{id}/decision", input, operation); first.EnsureSuccessStatusCode();
        using var replay = await PortalFactory.PostAdminAsync(admin, $"requests/{id}/decision", input, operation); replay.EnsureSuccessStatusCode();
        using var stale = await PortalFactory.PostAdminAsync(admin, $"requests/{id}/decision", input with { Decision = "Cancelled" }); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        detail = await Detail(admin, id);
        var payInput = new PortalPaymentConfirmInput(id, 1500, new(2026, 11, 17), "Transferencia", "QA-CONFIRM-IDEMPOTENT", "Verificación QA", detail.Request.Version); var payKey = Key();
        using var paid = await PortalFactory.PostAdminAsync(admin, "payments/confirm", payInput, payKey); paid.EnsureSuccessStatusCode(); var payment = (await paid.Content.ReadFromJsonAsync<PortalPaymentDto>())!;
        using var paidReplay = await PortalFactory.PostAdminAsync(admin, "payments/confirm", payInput, payKey); paidReplay.EnsureSuccessStatusCode();
        using var granted = await PortalFactory.PostAdminAsync(admin, "access", new PortalAccessInput(a.User.Id, id, new(2026, 11, 17), new(2026, 12, 17), "Enabled", "QA")); granted.EnsureSuccessStatusCode();
        Assert.True((await Account(buyer))!.Access.Allowed);
        using var voided = await PortalFactory.PostAdminAsync(admin, $"payments/{payment.Id}/void", new PortalPaymentVoidInput("Anulación ficticia QA", payment.Version)); voided.EnsureSuccessStatusCode();
        Assert.Equal("PAYMENT_PENDING", (await Account(buyer))!.Access.Reason);
        await app.InScopeAsync(async sp => { var db = sp.GetRequiredService<PortalDbContext>(); Assert.Equal(1, await db.Approvals.CountAsync()); Assert.Equal(1, await db.Payments.CountAsync()); });
    }

    [Fact]
    public async Task Confirmation_and_reset_tokens_are_one_use_and_reset_invalidates_old_session_without_changing_identity()
    {
        using var buyer = app.Buyer(); var account = await app.RegisterAsync(buyer, "reset@example.test");
        var confirmation = await app.LatestMailLinkAsync(account.User.Email); var confirmQuery = QueryHelpers.ParseQuery(confirmation.Query);
        var confirm = new PortalTokenInput(account.User.Id, confirmQuery["token"]!);
        using var confirmed = await PortalFactory.PostBuyerAsync(buyer, "auth/confirm-email", confirm); confirmed.EnsureSuccessStatusCode();
        using var again = await PortalFactory.PostBuyerAsync(buyer, "auth/confirm-email", confirm); Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        using var recovery = await PortalFactory.PostBuyerAsync(buyer, "auth/forgot-password", new PortalEmailInput(account.User.Email)); recovery.EnsureSuccessStatusCode();
        using var unknown = await PortalFactory.PostBuyerAsync(buyer, "auth/forgot-password", new PortalEmailInput("not-present@example.test"));
        Assert.Equal(await recovery.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var resetUri = await app.LatestMailLinkAsync(account.User.Email); Assert.Equal("/restablecer-acceso", resetUri.AbsolutePath);
        var query = QueryHelpers.ParseQuery(resetUri.Query); const string next = "New-Portal-Test-Only-Secret42!";
        var reset = new PortalResetInput(account.User.Id, query["token"]!, next, next);
        using var changed = await PortalFactory.PostBuyerAsync(buyer, "auth/reset-password", reset); changed.EnsureSuccessStatusCode();
        using var oldSession = await buyer.GetAsync("/portal-api/v1/auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        using var used = await PortalFactory.PostBuyerAsync(buyer, "auth/reset-password", reset); Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);
        using var oldPassword = await PortalFactory.PostBuyerAsync(buyer, "auth/login", new PortalLoginInput(account.User.Email, PortalFactory.Password)); Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var newPassword = await PortalFactory.PostBuyerAsync(buyer, "auth/login", new PortalLoginInput(account.User.Email, next)); newPassword.EnsureSuccessStatusCode();
        Assert.Equal(account.User.Id, (await Account(buyer))!.User.Id);
    }

    [Fact]
    public async Task Old_catalog_prices_and_unavailable_sources_cannot_register_a_purchase()
    {
        using var buyer = app.Buyer(); var initial = (await buyer.GetFromJsonAsync<PortalCatalogDto>("/portal-api/v1/catalog"))!;
        var input = Input(initial, "changed-plan@example.test");
        app.Catalog.Plans = app.Catalog.Plans.Select(p => p.Code == "plan-paid" ? p with { MonthlyPrice = 1600 } : p).ToArray();
        using var stale = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        app.Catalog.Available = false;
        using var unavailable = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input); Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        await app.InScopeAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<PortalDbContext>().Users.CountAsync()));
    }
}
