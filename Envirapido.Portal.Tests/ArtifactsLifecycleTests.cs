using System.Text;
using System.Text.Json;
using Envirapido.Portal.Api.Artifacts;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Envirapido.Portal.Tests;

public sealed class ArtifactsLifecycleTests
{
    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public async Task First_publication_accepts_the_supported_floor_but_never_a_downgrade(int code, bool supported)
    {
        using var app = new PortalFactory(); await app.InitializeAsync();
        await app.InScopeAsync(async services =>
        {
            var settings = services.GetRequiredService<IOptionsMonitor<PortalArtifactOptions>>();
            settings.CurrentValue.MinimumVersionCode = 9;
            var service = MakeService(services, new TestInspector(code));
            var artifact = await UploadAndValidate(service, services.GetRequiredService<PortalArtifactStorage>(), "floor-" + code);
            Assert.Equal(supported ? "Validated" : "Rejected", artifact.State);
            if (supported)
            {
                var published = await service.PublishAsync(artifact.Id,
                    new(Guid.NewGuid().ToString(), artifact.Version, true, "APK actual firmado comprobado"), "qa-superadmin", default);
                Assert.True(published.IsCurrent); Assert.Equal(9, published.VersionCode);
            }
            else
                Assert.Equal(409, (await Assert.ThrowsAsync<PortalArtifactException>(() => service.PublishAsync(artifact.Id,
                    new(Guid.NewGuid().ToString(), artifact.Version, true, "No se admite bajar versión"), "qa-superadmin", default))).StatusCode);
        });
    }

    [Fact]
    public async Task Publication_is_explicit_idempotent_and_never_reuses_a_withdrawn_versionCode()
    {
        using var app = new PortalFactory(); await app.InitializeAsync();
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<PortalDbContext>();
            var inspector = new TestInspector();
            var storage = services.GetRequiredService<PortalArtifactStorage>();
            var service = MakeService(services, inspector);
            var first = await UploadAndValidate(service, storage, "first");
            Assert.Equal("Validated", first.State);
            var request = new PortalArtifactPublishRequest(Guid.NewGuid().ToString(), first.Version, true, "QA funcional confirmada");
            var published = await service.PublishAsync(first.Id, request, "qa-superadmin", default);
            Assert.True(published.IsCurrent); Assert.Equal("Published", published.State);
            var beforeReplay = await db.AuditEvents.CountAsync();
            Assert.Equal(JsonSerializer.Serialize(published), JsonSerializer.Serialize(await service.PublishAsync(first.Id, request, "qa-superadmin", default)));
            Assert.Equal(beforeReplay, await db.AuditEvents.CountAsync());
            await service.WithdrawAsync(first.Id,
                new PortalArtifactWithdrawRequest(Guid.NewGuid().ToString(), published.Version, "Retirada de prueba"), "qa-superadmin", default);
            // An original successful response is replayed as recorded, but it
            // must never apply its old pointer/state after a later withdrawal.
            Assert.Equal(JsonSerializer.Serialize(published), JsonSerializer.Serialize(await service.PublishAsync(first.Id, request, "qa-superadmin", default)));
            Assert.Null(await service.CurrentIdAsync(default));
            Assert.Equal("Withdrawn", (await db.Artifacts.SingleAsync(x => x.Id == first.Id)).State);
            var secondSameCode = await UploadAndValidate(service, storage, "second");
            var lower = await Assert.ThrowsAsync<PortalArtifactException>(() => service.PublishAsync(secondSameCode.Id,
                new(Guid.NewGuid().ToString(), secondSameCode.Version, true, "Intento versión reutilizada"), "qa-superadmin", default));
            Assert.Equal(409, lower.StatusCode);
            Assert.Null(await service.CurrentIdAsync(default));
        });
    }

    [Fact]
    public async Task Published_bytes_require_unchanged_hash_and_functional_QA_acknowledgment()
    {
        using var app = new PortalFactory(); await app.InitializeAsync();
        await app.InScopeAsync(async services =>
        {
            var inspector = new TestInspector();
            var storage = services.GetRequiredService<PortalArtifactStorage>();
            var service = MakeService(services, inspector);
            var artifact = await UploadAndValidate(service, storage, "integrity");
            var noQa = await Assert.ThrowsAsync<PortalArtifactException>(() => service.PublishAsync(artifact.Id,
                new(Guid.NewGuid().ToString(), artifact.Version, false, "Sin confirmación QA"), "qa-superadmin", default));
            Assert.Equal(400, noQa.StatusCode);
            var path = storage.GetBlobPath(artifact.Sha256);
            var bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);
            var tampered = await Assert.ThrowsAsync<PortalArtifactException>(() => service.PublishAsync(artifact.Id,
                new(Guid.NewGuid().ToString(), artifact.Version, true, "QA de integridad"), "qa-superadmin", default));
            Assert.Equal(409, tampered.StatusCode);
            Assert.Null(await service.CurrentIdAsync(default));
        });
    }

    private static PortalArtifactService MakeService(IServiceProvider services, IApkInspector inspector) => new(
        services.GetRequiredService<PortalDbContext>(), services.GetRequiredService<PortalArtifactStorage>(), inspector,
        services.GetRequiredService<IOptionsMonitor<PortalArtifactOptions>>(), services.GetRequiredService<IHostEnvironment>(),
        services.GetRequiredService<TimeProvider>(), services.GetRequiredService<PortalAuditService>());

    private static async Task<PortalArtifactDto> UploadAndValidate(PortalArtifactService service, PortalArtifactStorage storage, string tag)
    {
        // The inspector is an explicit unit-test double. Real signature parsing
        // and real SDK verification are checked separately, never bypassed in app DI.
        var bytes = Encoding.UTF8.GetBytes("PK_testing_only_" + tag);
        var received = await storage.ReceiveAsync(new MemoryStream(bytes), default);
        var uploaded = await service.UploadAsync(received, "test-release.apk", "QA synthetic fixture", new string('a', 40),
            "https://envirapido.com", null, "qa-superadmin", Guid.NewGuid().ToString(), default);
        return await service.ValidateAsync(uploaded.Id, "qa-superadmin", default);
    }

    private sealed class TestInspector(int code = 10) : IApkInspector
    {
        public bool IsConfigured => true;
        public Task<ApkInspectionResult> InspectAsync(string path, CancellationToken cancellationToken) => Task.FromResult(
            new ApkInspectionResult(PortalArtifactOptions.PackageName, code == 9 ? "2.0.1" : "2.0.2", code, PortalArtifactOptions.OriginalCertificate, ["arm64-v8a"], 24, 36));
    }
}
