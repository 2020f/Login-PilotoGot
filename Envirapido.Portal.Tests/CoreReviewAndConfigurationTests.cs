using System.Net;
using System.Net.Http.Json;
using Envirapido.Portal.Api;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Envirapido.Portal.Tests;

public sealed class CoreReviewAndConfigurationTests
{
    [Fact]
    public async Task Reviewing_or_rejecting_a_report_requires_admin_is_idempotent_and_never_confirms_money()
    {
        using var app = new PortalFactory(); await app.InitializeAsync(); using var buyer = app.Buyer(); using var admin = app.Admin();
        var account = await app.RegisterAsync(buyer, "review-report@example.test"); var request = Assert.Single(account.Requests);
        using var reported = await PortalFactory.PostBuyerAsync(buyer, $"requests/{request.Id}/payment-reports", new PortalPaymentReportInput(1500, new(2026, 11, 17), "Transferencia", "QA-REPORTED-NOT-CONFIRMED"));
        reported.EnsureSuccessStatusCode(); var report = (await reported.Content.ReadFromJsonAsync<PortalPaymentReportDto>())!;
        Assert.Equal("Reported", report.Status);
        using var denied = await buyer.PostAsJsonAsync($"/portal-api/internal/admin/payment-reports/{report.Id}/decision", new PortalPaymentReportDecisionInput("InReview", "forged", report.Version));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var inReview = await PortalFactory.PostAdminAsync(admin, $"payment-reports/{report.Id}/decision", new PortalPaymentReportDecisionInput("InReview", "Verificar referencia QA", report.Version));
        inReview.EnsureSuccessStatusCode(); report = (await inReview.Content.ReadFromJsonAsync<PortalPaymentReportDto>())!;
        var input = new PortalPaymentReportDecisionInput("Rejected", "Referencia ficticia no validada", report.Version); var operation = Guid.NewGuid().ToString();
        using var rejected = await PortalFactory.PostAdminAsync(admin, $"payment-reports/{report.Id}/decision", input, operation); rejected.EnsureSuccessStatusCode();
        using var replay = await PortalFactory.PostAdminAsync(admin, $"payment-reports/{report.Id}/decision", input, operation); replay.EnsureSuccessStatusCode();
        report = (await rejected.Content.ReadFromJsonAsync<PortalPaymentReportDto>())!; Assert.Equal("Rejected", report.Status);
        using var fakeConfirmation = await PortalFactory.PostAdminAsync(admin, $"payment-reports/{report.Id}/decision", new PortalPaymentReportDecisionInput("Confirmed", "No crea un pago", report.Version));
        Assert.Equal(HttpStatusCode.BadRequest, fakeConfirmation.StatusCode);
        var after = (await buyer.GetFromJsonAsync<PortalAccountDto>("/portal-api/v1/account"))!;
        Assert.Equal("Rejected", Assert.Single(after.Requests).PaymentStatus); Assert.Equal("Referencia ficticia no validada", Assert.Single(after.Requests).PaymentReviewMessage); Assert.False(after.Access.Allowed);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PortalDbContext>(); Assert.Empty(await db.Payments.ToListAsync());
            Assert.Equal(2, await db.AuditEvents.CountAsync(a => a.Action == "PaymentReportDecision")); Assert.Equal("Submitted", (await db.Requests.SingleAsync()).Status);
        });
    }

    [Fact]
    public async Task Request_detail_keeps_received_profile_snapshot_when_current_profile_changes()
    {
        using var app = new PortalFactory(); await app.InitializeAsync(); using var buyer = app.Buyer(); using var admin = app.Admin();
        var account = await app.RegisterAsync(buyer, "snapshot-profile@example.test");
        var csrf = (await buyer.GetFromJsonAsync<PortalCsrf>("/portal-api/v1/csrf"))!;
        using var edit = new HttpRequestMessage(HttpMethod.Put, "/portal-api/v1/account") { Content = JsonContent.Create(account.Profile with { BusinessName = "Perfil actual actualizado", Phone = "5550000999" }) };
        edit.Headers.Add("X-CSRF-TOKEN", csrf.RequestToken); using var edited = await buyer.SendAsync(edit); edited.EnsureSuccessStatusCode();
        var detail = (await admin.GetFromJsonAsync<PortalAdminRequestDetail>($"/portal-api/internal/admin/requests/{Assert.Single(account.Requests).Id}"))!;
        Assert.Equal(account.Profile.BusinessName, detail.Profile.BusinessName); Assert.Equal(account.Profile.Phone, detail.Profile.Phone);
        Assert.Equal("Perfil actual actualizado", detail.CurrentProfile!.BusinessName);
    }

    [Fact]
    public async Task Withdraw_can_remove_a_stored_publication_after_its_source_plan_is_deactivated()
    {
        using var app = new PortalFactory(); await app.InitializeAsync(); using var admin = app.Admin();
        app.Catalog.Plans = app.Catalog.Plans.Where(p => p.Code != "plan-paid").ToArray();
        var list = (await admin.GetFromJsonAsync<CatalogEnvelope>("/portal-api/internal/admin/catalog"))!;
        var orphan = Assert.Single(list.Plans, p => p.Plan.Code == "plan-paid");
        Assert.False(orphan.SourceActive); Assert.True(orphan.IsPublished); Assert.Equal(1500, orphan.Plan.MonthlyPrice);
        using var withdrawn = await PortalFactory.PostAdminAsync(admin, "catalog", new PortalCatalogPublicationInput(orphan.Plan.Code, false, orphan.Plan.Description, 0, false, orphan.Version));
        withdrawn.EnsureSuccessStatusCode(); Assert.False((await withdrawn.Content.ReadFromJsonAsync<PortalAdminCatalogEntry>())!.IsPublished);
        using var buyer = app.Buyer(); var catalog = (await buyer.GetFromJsonAsync<PortalCatalogDto>("/portal-api/v1/catalog"))!;
        Assert.DoesNotContain(catalog.Plans, p => p.Code == "plan-paid");
    }

    [Fact]
    public async Task Forwarded_client_IP_and_protocol_only_apply_from_exact_trusted_proxies()
    {
        using var app = new PortalFactory(); await app.InitializeAsync();
        var trusted = await app.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback; context.Request.Scheme = "http";
            context.Request.Host = new HostString("portal.example.test"); context.Request.Path = "/portal-api/health";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.20, 198.51.100.44"; context.Request.Headers["X-Forwarded-Proto"] = "http, https";
        });
        Assert.Equal("198.51.100.44", trusted.Connection.RemoteIpAddress!.ToString()); Assert.True(trusted.Request.IsHttps);
        Assert.Equal("203.0.113.20", trusted.Request.Headers["X-Forwarded-For"].ToString());
        var forged = await app.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.99"); context.Request.Scheme = "http";
            context.Request.Host = new HostString("portal.example.test"); context.Request.Path = "/portal-api/health";
            context.Request.Headers["X-Forwarded-For"] = "198.51.100.44"; context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        Assert.Equal("203.0.113.99", forged.Connection.RemoteIpAddress!.ToString()); Assert.False(forged.Request.IsHttps);
    }

    [Fact]
    public void Production_allows_private_loopback_catalog_but_rejects_public_HTTP_remote_HTTP_and_example_secrets()
    {
        var env = new ProductionEnvironment(); const string connection = "Server=(localdb)\\PortalDesign;Database=EnvirapidoPortalTest_config;Integrated Security=true";
        PortalOptions Options() => new() { AdminKey = "private-portal-admin-Q4uqJ9NUv3fi1Xsc7", CatalogKey = "private-catalog-L8eMg5Qa6Yhw1CzV0", DataProtectionDirectory = Path.Combine(Path.GetTempPath(), "portal-config-validation"), PublicBaseUrl = "https://portal.envirapido.com", CatalogUrl = "http://127.0.0.1:5008/api/internal/portal/catalog" };
        Options().Validate(env, connection);
        var publicHttp = Options(); publicHttp.PublicBaseUrl = "http://localhost:5031"; Assert.Throws<InvalidOperationException>(() => publicHttp.Validate(env, connection));
        var remote = Options(); remote.CatalogUrl = "http://crm.example.test/api/internal/portal/catalog"; Assert.Throws<InvalidOperationException>(() => remote.Validate(env, connection));
        var copiedExample = Options(); copiedExample.AdminKey = "REEMPLAZAR_CON_SECRETO_ALEATORIO_ADMIN"; Assert.Throws<InvalidOperationException>(() => copiedExample.Validate(env, connection));
    }
    private sealed record CatalogEnvelope(IReadOnlyList<PortalAdminCatalogEntry> Plans);
    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "PortalConfigTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
