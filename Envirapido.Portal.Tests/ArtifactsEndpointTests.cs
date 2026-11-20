using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Envirapido.Portal.Api.Artifacts;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Envirapido.Portal.Tests;

public sealed class ArtifactsEndpointTests
{
    [Fact]
    public async Task Upload_is_admin_only_private_quarantined_and_idempotent_without_publishing()
    {
        using var app = new PortalFactory(); await app.InitializeAsync();
        using var buyer = app.Buyer(); await app.RegisterAsync(buyer);
        using var admin = app.Admin();
        var bytes = new byte[] { 80, 75, 3, 4, 10, 20, 30 };
        var operation = Guid.NewGuid().ToString();
        using var denied = await Upload(buyer, bytes, operation);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var uploaded = await Upload(admin, bytes, operation);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var artifact = (await uploaded.Content.ReadFromJsonAsync<PortalArtifactDto>())!;
        Assert.Equal("Quarantined", artifact.State); Assert.False(artifact.IsCurrent);
        Assert.Null(artifact.VersionCode); Assert.Null(artifact.CertificateSha256);
        using var replay = await Upload(admin, bytes, operation);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(artifact.Id, (await replay.Content.ReadFromJsonAsync<PortalArtifactDto>())!.Id);
        using var duplicate = await Upload(admin, bytes, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var invalidPublish = await PortalFactory.PostAdminAsync(admin, $"artifacts/{artifact.Id}/publish",
            new PortalArtifactPublishRequest(Guid.NewGuid().ToString(), artifact.Version, true, "QA test no validado"));
        Assert.Equal(HttpStatusCode.Conflict, invalidPublish.StatusCode);
        using var pending = await buyer.GetAsync("/portal-api/v1/downloads/current");
        Assert.Equal(HttpStatusCode.Forbidden, pending.StatusCode);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<PortalDbContext>();
            Assert.Equal(1, await db.Artifacts.CountAsync());
            Assert.False(await db.Set<PortalArtifactChannel>().AnyAsync());
            Assert.False(await db.Set<PortalDownloadEvent>().AnyAsync());
        });
    }

    [Fact]
    public async Task Every_file_and_Range_request_rechecks_access_and_expiration_even_with_a_known_URL()
    {
        using var app = new PortalFactory(); await app.InitializeAsync();
        using var buyer = app.Buyer();
        var account = await app.RegisterAsync(buyer, "download-qa@example.test", "plan-free");
        await app.ConfirmEmailAsync(buyer, "download-qa@example.test");
        using var remembered = await PortalFactory.PostBuyerAsync(buyer, "auth/login",
            new PortalLoginInput("download-qa@example.test", PortalFactory.Password, true));
        remembered.EnsureSuccessStatusCode();
        using var admin = app.Admin();
        var request = Assert.Single(account.Requests);
        using var approved = await PortalFactory.PostAdminAsync(admin, $"requests/{request.Id}/decision",
            new PortalDecisionInput("Approved", "Solicitud QA aprobada", request.Version));
        approved.EnsureSuccessStatusCode();
        var today = PortalAccessService.Today(app.Clock);
        using var enabled = await PortalFactory.PostAdminAsync(admin, "access",
            new PortalAccessInput(account.User.Id, request.Id, today, today, "Enabled", "Vigencia QA de un día"));
        enabled.EnsureSuccessStatusCode();
        var published = await SeedPublishedFixture(app);
        using var anonymous = app.Buyer();
        using var noSession = await anonymous.GetAsync(published.Dto.DownloadPath);
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        using var other = app.Buyer(); await app.RegisterAsync(other, "other-download-qa@example.test", "plan-free");
        using var otherDenied = await other.GetAsync(published.Dto.DownloadPath);
        Assert.Equal(HttpStatusCode.Forbidden, otherDenied.StatusCode);
        using var metadata = await buyer.GetAsync("/portal-api/v1/downloads/current");
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        var current = (await metadata.Content.ReadFromJsonAsync<PortalDownloadDto>())!;
        Assert.Equal(published.Dto.Id, current.Id);
        using var range = new HttpRequestMessage(HttpMethod.Get, current.DownloadPath);
        range.Headers.Range = new RangeHeaderValue(2, 5);
        using var part = await buyer.SendAsync(range);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(published.Bytes[2..6], await part.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", part.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("no-store", part.Headers.CacheControl!.ToString());
        Assert.Equal("application/vnd.android.package-archive", part.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, part.Content.Headers.ContentRange?.From);
        Assert.Equal(5, part.Content.Headers.ContentRange?.To);
        app.Clock.Set(PortalAccessService.Start(today.AddDays(1)));
        using var expiredRange = new HttpRequestMessage(HttpMethod.Get, current.DownloadPath);
        expiredRange.Headers.Range = new RangeHeaderValue(2, 5);
        using var deniedAfterExpiration = await buyer.SendAsync(expiredRange);
        Assert.Equal(HttpStatusCode.Forbidden, deniedAfterExpiration.StatusCode);
        Assert.Equal("application/json", deniedAfterExpiration.Content.Headers.ContentType?.MediaType);
        Assert.Null(deniedAfterExpiration.Content.Headers.ContentRange);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<PortalDbContext>();
            Assert.Equal(1, await db.Set<PortalDownloadEvent>().CountAsync());
            Assert.True((await db.Set<PortalDownloadEvent>().SingleAsync()).RangeRequested);
        });
    }

    [Fact]
    public async Task Withdrawal_preserves_the_high_water_mark_and_removes_the_current_download()
    {
        using var app = new PortalFactory(); await app.InitializeAsync();
        var fixture = await SeedPublishedFixture(app);
        using var admin = app.Admin();
        using var withdrawal = await PortalFactory.PostAdminAsync(admin, $"artifacts/{fixture.Dto.Id}/withdraw",
            new PortalArtifactWithdrawRequest(Guid.NewGuid().ToString(), fixture.Version, "Retiro QA autorizado"));
        Assert.Equal(HttpStatusCode.OK, withdrawal.StatusCode);
        var withdrawn = (await withdrawal.Content.ReadFromJsonAsync<PortalArtifactDto>())!;
        Assert.Equal("Withdrawn", withdrawn.State); Assert.False(withdrawn.IsCurrent);
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<PortalDbContext>();
            var channel = await db.Set<PortalArtifactChannel>().SingleAsync();
            Assert.Null(channel.CurrentArtifactId);
            Assert.Equal(10, channel.MaximumPublishedVersionCode);
            var failure = await Assert.ThrowsAsync<PortalArtifactException>(() => services.GetRequiredService<PortalArtifactService>().CurrentDownloadAsync(default));
            Assert.Equal(404, failure.StatusCode);
        });
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] bytes, string operation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/portal-api/internal/admin/artifacts/upload");
        request.Headers.Add("Idempotency-Key", operation);
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "Envirapido-release.apk");
        form.Add(new StringContent("Archivo sintético exclusivo de prueba"), "notes");
        form.Add(new StringContent(new string('a', 40)), "sourceCommit");
        form.Add(new StringContent("https://envirapido.com"), "publicBaseUrl");
        request.Content = form;
        return await client.SendAsync(request);
    }

    private static async Task<(PortalDownloadDto Dto, byte[] Bytes, string Version)> SeedPublishedFixture(PortalFactory app)
    {
        // Synthetic private bytes are seeded only to test HTTP authorization and
        // Range behavior. This does not substitute the separate real APK check.
        var bytes = Encoding.ASCII.GetBytes("PK_fixture_for_private_range_tests");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var artifact = new PortalArtifact { State = "Published", OriginalFileName = "qa-only.apk", VersionName = "2.0.2", VersionCode = 10,
            Sha256 = sha, Bytes = bytes.Length, PackageName = PortalArtifactOptions.PackageName,
            CertificateSha256 = PortalArtifactOptions.OriginalCertificate, CreatedAt = app.Clock.GetUtcNow(),
            ValidatedAt = app.Clock.GetUtcNow(), PublishedAt = app.Clock.GetUtcNow(), SourceCommit = new string('a', 40), PublicBaseUrl = "https://envirapido.com" };
        await app.InScopeAsync(async services =>
        {
            var storage = services.GetRequiredService<PortalArtifactStorage>();
            var received = await storage.ReceiveAsync(new MemoryStream(bytes), default);
            storage.FinalizeBlob(received);
            var db = services.GetRequiredService<PortalDbContext>();
            db.Artifacts.Add(artifact);
            db.Set<PortalArtifactChannel>().Add(new() { CurrentArtifactId = artifact.Id, MaximumPublishedVersionCode = 10 });
            await db.SaveChangesAsync();
        });
        return (new(artifact.Id, "2.0.2", 10, bytes.Length, sha, "Envirapido-android-10-release.apk",
            $"/portal-api/v1/downloads/{artifact.Id}/file", artifact.PublishedAt.Value), bytes, artifact.Version);
    }
}
