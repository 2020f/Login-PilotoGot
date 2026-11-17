using Envirapido.Portal.Api.Artifacts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace Envirapido.Portal.Tests;

public sealed class ArtifactsStorageTests
{
    [Fact]
    public async Task Storage_writes_content_addressed_immutable_blobs_outside_the_application()
    {
        using var area = new ArtifactTestArea();
        var storage = area.Storage;
        var bytes = new byte[] { 80, 75, 3, 4, 1, 2, 3 };
        var first = await storage.ReceiveAsync(new MemoryStream(bytes), default);
        Assert.True(File.Exists(first.TemporaryPath));
        storage.FinalizeBlob(first);
        var path = storage.GetBlobPath(first.Sha256);
        Assert.Equal(first.Sha256 + ".apk", Path.GetFileName(path));
        Assert.False(File.Exists(first.TemporaryPath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var duplicate = await storage.ReceiveAsync(new MemoryStream(bytes), default);
        storage.FinalizeBlob(duplicate);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        await storage.VerifyBlobAsync(first.Sha256, first.Bytes, default);
        await File.WriteAllBytesAsync(path, [80, 75, 3, 4, 1, 2, 4]);
        var failure = await Assert.ThrowsAsync<PortalArtifactException>(() => storage.VerifyBlobAsync(first.Sha256, first.Bytes, default));
        Assert.Equal(409, failure.StatusCode);
    }

    [Fact]
    public async Task Upload_limit_removes_only_its_own_quarantine_partial()
    {
        using var area = new ArtifactTestArea();
        area.Options.Value.MaxUploadBytes = 1024 * 1024;
        var failure = await Assert.ThrowsAsync<PortalArtifactException>(() => area.Storage.ReceiveAsync(new MemoryStream(new byte[1024 * 1024 + 1]), default));
        Assert.Equal(413, failure.StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(area.Private, "quarantine")));
        var sentinel = Path.Combine(area.Root, "outside.upload");
        await File.WriteAllTextAsync(sentinel, "preserve");
        area.Storage.DeleteTemporary(sentinel);
        Assert.True(File.Exists(sentinel));
    }

    [Fact]
    public void Storage_inside_the_application_and_arbitrary_blob_names_are_rejected()
    {
        using var area = new ArtifactTestArea();
        Assert.True(area.Storage.IsConfigured);
        Assert.Throws<PortalArtifactException>(() => area.Storage.GetBlobPath("../../outside"));
        area.Options.Value.StoragePath = Path.Combine(area.Content, "wwwroot", "uploads");
        Assert.False(area.Storage.IsConfigured);
        Assert.Throws<PortalArtifactException>(() => area.Storage.GetBlobPath(new string('a', 64)));
        area.Options.Value.StoragePath = "relative-private-files";
        Assert.False(area.Storage.IsConfigured);
    }
}

internal sealed class ArtifactTestArea : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "PortalArtifactTest_" + Guid.NewGuid().ToString("N"));
    public string Content => Path.Combine(Root, "application");
    public string Private => Path.Combine(Root, "private");
    public ArtifactOptionsMonitor Options { get; }
    public ArtifactTestEnvironment Environment { get; }
    public PortalArtifactStorage Storage { get; }
    public ArtifactTestArea()
    {
        Directory.CreateDirectory(Content);
        File.WriteAllText(Path.Combine(Root, ".owned-artifact-test"), "test-only");
        Environment = new() { ContentRootPath = Content, WebRootPath = Path.Combine(Content, "wwwroot") };
        Options = new() { Value = new PortalArtifactOptions { StoragePath = Private } };
        Storage = new(Options, Environment);
    }
    public void Dispose()
    {
        // Only this fixture's newly-created GUID directory may be removed.
        var resolved = Path.GetFullPath(Root);
        if (Path.GetDirectoryName(resolved) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
            || !Path.GetFileName(resolved).StartsWith("PortalArtifactTest_", StringComparison.Ordinal)
            || !File.Exists(Path.Combine(resolved, ".owned-artifact-test")))
            throw new InvalidOperationException("La carpeta temporal no pertenece a esta prueba.");
        Directory.Delete(resolved, recursive: true);
    }
}

internal sealed class ArtifactOptionsMonitor : IOptionsMonitor<PortalArtifactOptions>
{
    public PortalArtifactOptions Value { get; set; } = new();
    public PortalArtifactOptions CurrentValue => Value;
    public PortalArtifactOptions Get(string? name) => Value;
    public IDisposable? OnChange(Action<PortalArtifactOptions, string?> listener) => null;
}

internal sealed class ArtifactTestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "PortalArtifactsTests";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "wwwroot");
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
