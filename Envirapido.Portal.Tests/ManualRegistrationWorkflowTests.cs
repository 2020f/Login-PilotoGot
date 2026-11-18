using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Envirapido.Portal.Api;
using Envirapido.Portal.Api.Artifacts;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Envirapido.Portal.Tests;

public sealed class ManualRegistrationWorkflowTests : IAsyncLifetime
{
    private readonly PortalFactory database = new(false);
    private readonly NeverSendMail mail = new();
    private WebApplicationFactory<PortalProgram> app = null!;
    private static string Key() => Guid.NewGuid().ToString();

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        app = PolicyHost(database);
    }

    public Task DisposeAsync()
    {
        app?.Dispose();
        database.Dispose();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<PortalProgram> PolicyHost(PortalFactory owner)
        => owner.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Portal:Mail:Enabled", "false");
            builder.ConfigureTestServices(services =>
            {
                // Only the mail availability guard observes this environment.
                // The HTTP host stays Testing/TestServer, never a public
                // Production listener. No SMTP/network transport is used.
                services.RemoveAll<PortalMailService>();
                services.AddScoped(sp => new PortalMailService(
                    sp.GetRequiredService<PortalDbContext>(),
                    sp.GetRequiredService<IDataProtectionProvider>(),
                    sp.GetRequiredService<PortalOptions>(),
                    new NonLocalMailEnvironment(),
                    sp.GetRequiredService<TimeProvider>()));
                services.RemoveAll<IPortalMailTransport>();
                services.AddSingleton<IPortalMailTransport>(mail);
            });
        });

    private static HttpClient Buyer(WebApplicationFactory<PortalProgram> host)
        => host.CreateClient(new() { BaseAddress = new Uri("https://portal.example.test"), AllowAutoRedirect = false });

    private HttpClient Admin()
    {
        var client = Buyer(app);
        client.DefaultRequestHeaders.Add("X-Envirapido-Portal-Key", PortalFactory.AdminKey);
        client.DefaultRequestHeaders.Add("X-Envirapido-Operator", "qa-manual-superadmin");
        return client;
    }

    private static async Task<PortalRegisterInput> Input(HttpClient buyer, string email, string planCode = "plan-paid")
    {
        var catalog = (await buyer.GetFromJsonAsync<PortalCatalogDto>("/portal-api/v1/catalog"))!;
        var plan = Assert.Single(catalog.Plans, p => p.Code == planCode);
        return new(email, PortalFactory.Password, PortalFactory.Password, plan.Code, plan.Version,
            new("Responsable manual QA", "Empresa ficticia manual", "5550000199", "Distrito Nacional", "Santo Domingo", "Logística"),
            catalog.PolicyVersions.Terms, catalog.PolicyVersions.Privacy);
    }

    private static async Task<PortalAccountDto> Account(HttpClient buyer)
        => (await buyer.GetFromJsonAsync<PortalAccountDto>("/portal-api/v1/account"))!;

    private static async Task<PortalAdminRequestDetail> Detail(HttpClient admin, Guid requestId)
        => (await admin.GetFromJsonAsync<PortalAdminRequestDetail>("/portal-api/internal/admin/requests/" + requestId))!;

    private async Task InScope(Func<PortalDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        if (db.Database.IsSqlServer()) Assert.Equal(database.DatabaseName, db.Database.GetDbConnection().Database);
        await action(db);
    }

    private static async Task AssertDownloadDenied(HttpClient buyer, string? path = null)
    {
        using var metadata = await buyer.GetAsync("/portal-api/v1/downloads/current");
        Assert.Equal(HttpStatusCode.Forbidden, metadata.StatusCode);
        if (path is not null)
        {
            using var range = new HttpRequestMessage(HttpMethod.Get, path);
            range.Headers.Range = new RangeHeaderValue(0, 3);
            using var file = await buyer.SendAsync(range);
            Assert.Equal(HttpStatusCode.Forbidden, file.StatusCode);
            Assert.Null(file.Content.Headers.ContentRange);
        }
    }

    private async Task AssertNoMailOrConfirmationTokens()
    {
        Assert.Equal(0, mail.Calls);
        await InScope(async db =>
        {
            Assert.Empty(await db.Tokens.ToListAsync());
            Assert.Empty(await db.Outbox.ToListAsync());
        });
    }

    [Fact]
    public async Task Manual_registration_without_SMTP_is_idempotent_and_does_not_activate_a_plan_or_verify_the_address()
    {
        var policy = app.Services.GetRequiredService<PortalOptions>();
        Assert.False(policy.RequireEmailConfirmation);
        Assert.False(policy.Mail.Enabled);
        using var buyer = Buyer(app);
        var publicPolicy = (await buyer.GetFromJsonAsync<PortalAuthOptions>("/portal-api/v1/auth/options"))!;
        Assert.False(publicPolicy.EmailConfirmationRequired);
        Assert.False(publicPolicy.PasswordRecoveryAvailable);
        var input = await Input(buyer, "manual-register@example.test");
        var operation = Key();
        using var registered = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input, operation);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var account = (await registered.Content.ReadFromJsonAsync<PortalAccountDto>())!;
        Assert.False(account.User.EmailConfirmed);
        Assert.False(account.User.EmailConfirmationRequired);
        Assert.Equal("Active", account.User.Status);
        Assert.False(account.Access.Allowed);
        Assert.Equal("REQUEST_PENDING", account.Access.Reason);
        Assert.Null(account.Access.CrmUrl);
        Assert.Equal("Submitted", Assert.Single(account.Requests).Status);
        Assert.Equal("NotReported", account.Requests[0].PaymentStatus);

        using var replay = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input, operation);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(account.User.Id, (await replay.Content.ReadFromJsonAsync<PortalAccountDto>())!.User.Id);
        using var resend = await PortalFactory.PostBuyerAsync(buyer, "auth/resend-confirmation", new { });
        resend.EnsureSuccessStatusCode();
        Assert.Contains("administr", await resend.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var logout = await PortalFactory.PostBuyerAsync(buyer, "auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var login = await PortalFactory.PostBuyerAsync(buyer, "auth/login",
            new PortalLoginInput(input.Email, input.Password, true));
        login.EnsureSuccessStatusCode();
        Assert.False((await Account(buyer)).Access.Allowed);
        await AssertDownloadDenied(buyer);
        await AssertNoMailOrConfirmationTokens();
        await InScope(async db =>
        {
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(1, await db.Profiles.CountAsync());
            Assert.Equal(1, await db.Requests.CountAsync());
            Assert.Equal(1, await db.Operations.CountAsync(o => o.Scope == "register:" + input.Email));
            Assert.Empty(await db.AccessGrants.ToListAsync());
            Assert.Empty(await db.Payments.ToListAsync());
            Assert.All(db.Model.GetEntityTypes(), entity =>
            {
                Assert.StartsWith("Envirapido.Portal.Api", entity.ClrType.Namespace);
                Assert.DoesNotContain(entity.GetProperties(), p => p.Name is "ClienteAppId" or "IdentityUserId" or "TiendaId" or "PilotoId");
            });
        });
    }

    [Fact]
    public async Task Manual_activation_still_requires_admin_payment_and_vigency_and_rechecks_every_download()
    {
        using var buyer = Buyer(app);
        using var admin = Admin();
        var input = await Input(buyer, "manual-window@example.test");
        using var registration = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input);
        registration.EnsureSuccessStatusCode();
        var account = (await registration.Content.ReadFromJsonAsync<PortalAccountDto>())!;
        var request = Assert.Single(account.Requests);
        using var remembered = await PortalFactory.PostBuyerAsync(buyer, "auth/login",
            new PortalLoginInput(input.Email, input.Password, true));
        remembered.EnsureSuccessStatusCode();
        var published = await SeedPrivateDownloadFixture();
        var today = PortalAccessService.Today(database.Clock);
        var starts = today.AddDays(1);
        var ends = today.AddDays(3);

        using var forged = await buyer.PostAsJsonAsync("/portal-api/internal/admin/access",
            new PortalAccessInput(account.User.Id, request.Id, starts, ends, "Enabled", "No autorizado"));
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        using var premature = await PortalFactory.PostAdminAsync(admin, "access",
            new PortalAccessInput(account.User.Id, request.Id, starts, ends, "Enabled", "QA aún sin aprobar"));
        Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
        using var approved = await PortalFactory.PostAdminAsync(admin, $"requests/{request.Id}/decision",
            new PortalDecisionInput("Approved", "Solicitud manual ficticia aprobada", request.Version));
        approved.EnsureSuccessStatusCode();
        Assert.False((await Account(buyer)).Access.Allowed);
        using var enabled = await PortalFactory.PostAdminAsync(admin, "access",
            new PortalAccessInput(account.User.Id, request.Id, starts, ends, "Enabled", "Ventana manual futura"));
        enabled.EnsureSuccessStatusCode();
        Assert.Equal("PAYMENT_PENDING", (await Account(buyer)).Access.Reason);
        await AssertDownloadDenied(buyer, published.Path);

        var beforePayment = await Detail(admin, request.Id);
        using var paid = await PortalFactory.PostAdminAsync(admin, "payments/confirm",
            new PortalPaymentConfirmInput(request.Id, 1500m, today, "Transferencia", "QA-MANUAL-PAID-1", "Pago externo ficticio para prueba", beforePayment.Request.Version));
        paid.EnsureSuccessStatusCode();
        var payment = (await paid.Content.ReadFromJsonAsync<PortalPaymentDto>())!;
        Assert.Equal("NOT_STARTED", (await Account(buyer)).Access.Reason);
        await AssertDownloadDenied(buyer, published.Path);

        database.Clock.Set(PortalAccessService.Start(starts));
        account = await Account(buyer);
        Assert.False(account.User.EmailConfirmed);
        Assert.True(account.Access.Allowed);
        Assert.Equal("ACTIVE", account.Access.Reason);
        Assert.Equal(PortalOptions.CrmLoginUrl, account.Access.CrmUrl);
        var activeSummary = (await admin.GetFromJsonAsync<PortalAdminSummary>("/portal-api/internal/admin/summary"))!;
        Assert.Equal(1, activeSummary.ActiveAccess);
        using var metadata = await buyer.GetAsync("/portal-api/v1/downloads/current");
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        using var download = await buyer.GetAsync(published.Path);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(published.Bytes, await download.Content.ReadAsByteArrayAsync());

        var current = await Detail(admin, request.Id);
        using var suspend = await PortalFactory.PostAdminAsync(admin, "access",
            new PortalAccessInput(account.User.Id, request.Id, starts, ends, "Suspended", "Pausa manual de QA", current.AccessGrant!.Version));
        suspend.EnsureSuccessStatusCode();
        Assert.Equal("SUSPENDED", (await Account(buyer)).Access.Reason);
        Assert.Null((await Account(buyer)).Access.CrmUrl);
        await AssertDownloadDenied(buyer, published.Path);
        current = await Detail(admin, request.Id);
        using var resume = await PortalFactory.PostAdminAsync(admin, "access",
            new PortalAccessInput(account.User.Id, request.Id, starts, ends, "Enabled", "Reactivación manual de QA", current.AccessGrant!.Version));
        resume.EnsureSuccessStatusCode();
        Assert.True((await Account(buyer)).Access.Allowed);

        using var voided = await PortalFactory.PostAdminAsync(admin, $"payments/{payment.Id}/void",
            new PortalPaymentVoidInput("Anulación ficticia de QA", payment.Version));
        voided.EnsureSuccessStatusCode();
        Assert.Equal("PAYMENT_PENDING", (await Account(buyer)).Access.Reason);
        Assert.Null((await Account(buyer)).Access.CrmUrl);
        await AssertDownloadDenied(buyer, published.Path);
        current = await Detail(admin, request.Id);
        using var replacement = await PortalFactory.PostAdminAsync(admin, "payments/confirm",
            new PortalPaymentConfirmInput(request.Id, 1500m, PortalAccessService.Today(database.Clock), "Transferencia", "QA-MANUAL-PAID-2", "Pago externo sustituto ficticio", current.Request.Version));
        replacement.EnsureSuccessStatusCode();
        Assert.True((await Account(buyer)).Access.Allowed);

        database.Clock.Set(PortalAccessService.Start(ends.AddDays(1)));
        account = await Account(buyer);
        Assert.Equal("EXPIRED", account.Access.Reason);
        Assert.Null(account.Access.CrmUrl);
        await AssertDownloadDenied(buyer, published.Path);
        var expiredSummary = (await admin.GetFromJsonAsync<PortalAdminSummary>("/portal-api/internal/admin/summary"))!;
        Assert.Equal(0, expiredSummary.ActiveAccess);
        Assert.Equal(1, expiredSummary.ExpiredAccess);
        await AssertNoMailOrConfirmationTokens();
    }

    [Fact]
    public async Task Optional_email_verification_mode_still_rolls_back_disabled_mail_and_reuses_registration_operation()
    {
        using var verificationDatabase = new PortalFactory();
        await verificationDatabase.InitializeAsync();
        using var verification = PolicyHost(verificationDatabase);
        using var buyer = Buyer(verification);
        var options = verification.Services.GetRequiredService<PortalOptions>();
        Assert.True(options.RequireEmailConfirmation);
        Assert.False(options.Mail.Enabled);
        var input = await Input(buyer, "optional-confirmation@example.test");
        var operation = Key();
        using var unavailable = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input, operation);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("EMAIL_UNAVAILABLE", (await unavailable.Content.ReadFromJsonAsync<PortalError>())!.Code);
        using (var scope = verification.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Empty(await db.Profiles.ToListAsync());
            Assert.Empty(await db.Requests.ToListAsync());
            Assert.Empty(await db.Tokens.ToListAsync());
            Assert.Empty(await db.Outbox.ToListAsync());
            Assert.Empty(await db.Sessions.ToListAsync());
            Assert.False(await db.Operations.AnyAsync(o => o.Scope == "register:" + input.Email));
        }
        // A configured transport is simulated, never a real SMTP connection.
        options.Mail.Host = "smtp.example.test";
        options.Mail.FromAddress = "no-reply@example.test";
        options.Mail.Enabled = true;
        using var recovered = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input, operation);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        var account = (await recovered.Content.ReadFromJsonAsync<PortalAccountDto>())!;
        Assert.False(account.User.EmailConfirmed);
        Assert.True(account.User.EmailConfirmationRequired);
        Assert.Equal("EMAIL_UNCONFIRMED", account.Access.Reason);
        Assert.False(account.Access.Allowed);
        using var replay = await PortalFactory.PostBuyerAsync(buyer, "auth/register", input, operation);
        Assert.Equal(account.User.Id, (await replay.Content.ReadFromJsonAsync<PortalAccountDto>())!.User.Id);
        using (var scope = verification.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(1, await db.Requests.CountAsync());
            Assert.Equal(1, await db.Tokens.CountAsync());
            Assert.Equal(1, await db.Outbox.CountAsync());
            Assert.Equal(1, await db.Operations.CountAsync(o => o.Scope == "register:" + input.Email));
        }
        Assert.Equal(0, mail.Calls);
    }

    private async Task<(string Path, byte[] Bytes)> SeedPrivateDownloadFixture()
    {
        // Synthetic bytes exercise authorization only. This is not a signed APK
        // fixture and does not substitute release inspection or publication QA.
        var bytes = Encoding.ASCII.GetBytes("PK_manual_approval_private_download_fixture");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var artifact = new PortalArtifact
        {
            State = "Published", OriginalFileName = "qa-manual-only.apk", VersionName = "2.0.2", VersionCode = 10,
            Sha256 = sha, Bytes = bytes.Length, PackageName = PortalArtifactOptions.PackageName,
            CertificateSha256 = PortalArtifactOptions.OriginalCertificate, CreatedAt = database.Clock.GetUtcNow(),
            ValidatedAt = database.Clock.GetUtcNow(), PublishedAt = database.Clock.GetUtcNow(),
            SourceCommit = new string('a', 40), PublicBaseUrl = "https://envirapido.com"
        };
        using var scope = app.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<PortalArtifactStorage>();
        var received = await storage.ReceiveAsync(new MemoryStream(bytes), default);
        storage.FinalizeBlob(received);
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.Artifacts.Add(artifact);
        db.Set<PortalArtifactChannel>().Add(new() { CurrentArtifactId = artifact.Id, MaximumPublishedVersionCode = 10 });
        await db.SaveChangesAsync();
        return ($"/portal-api/v1/downloads/{artifact.Id}/file", bytes);
    }

    private sealed class NeverSendMail : IPortalMailTransport
    {
        public int Calls { get; private set; }
        public Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("No SMTP transmission is authorized in this test.");
        }
    }

    private sealed class NonLocalMailEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "PortalMailAvailabilityGuardTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
