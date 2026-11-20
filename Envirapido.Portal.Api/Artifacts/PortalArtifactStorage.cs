using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Envirapido.Portal.Api.Artifacts;

public sealed class PortalArtifactStorage(IOptionsMonitor<PortalArtifactOptions> options, IWebHostEnvironment environment)
{
    public bool IsConfigured => TryRoot(out _);

    private bool TryRoot(out string root)
    {
        root = "";
        var configured = options.CurrentValue.StoragePath;
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured)) return false;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
            var content = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.ContentRootPath));
            var web = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.WebRootPath ?? Path.Combine(content, "wwwroot")));
            return root != Path.GetPathRoot(root) && !IsWithin(root, content) && !IsWithin(root, web);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static bool IsWithin(string candidate, string root) =>
        candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private string Root()
    {
        if (!TryRoot(out var root)) throw new PortalArtifactException(503, "El almacenamiento privado de versiones no está configurado.");
        return root;
    }

    public string GetBlobPath(string sha256)
    {
        if (!Regex.IsMatch(sha256, "^[a-f0-9]{64}$")) throw new PortalArtifactException(404, "Versión no disponible.");
        return Path.Combine(Root(), "blobs", sha256 + ".apk");
    }

    public async Task<ReceivedArtifact> ReceiveAsync(Stream source, CancellationToken cancellationToken)
    {
        var root = Root();
        var quarantine = Path.Combine(root, "quarantine");
        Directory.CreateDirectory(quarantine);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(quarantine, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = Path.Combine(quarantine, Guid.NewGuid().ToString("N") + ".upload");
        long bytes = 0;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    bytes += read;
                    if (bytes > options.CurrentValue.UploadLimit) throw new PortalArtifactException(413, "El APK supera el tamaño máximo permitido.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await target.FlushAsync(cancellationToken);
            }
            if (bytes < 4) throw new PortalArtifactException(400, "El archivo no es un APK válido.");
            return new ReceivedArtifact(temporary, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), bytes);
        }
        catch { DeleteTemporary(temporary); throw; }
    }

    public void FinalizeBlob(ReceivedArtifact artifact)
    {
        var target = GetBlobPath(artifact.Sha256);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try { File.Move(artifact.TemporaryPath, target, false); }
        catch (IOException) when (File.Exists(target))
        {
            // Content addressed: a concurrent upload can already have written
            // these same immutable bytes. Never replace a previously stored blob.
            DeleteTemporary(artifact.TemporaryPath);
        }
    }

    public void DeleteTemporary(string path)
    {
        if (!TryRoot(out var root)) return;
        var quarantine = Path.Combine(root, "quarantine");
        var resolved = Path.GetFullPath(path);
        if (Path.GetDirectoryName(resolved) == quarantine
            && Regex.IsMatch(Path.GetFileName(resolved), "^[a-f0-9]{32}\\.upload$"))
            File.Delete(resolved);
    }

    public async Task VerifyBlobAsync(string sha256, long bytes, CancellationToken cancellationToken)
    {
        var path = GetBlobPath(sha256);
        if (!File.Exists(path) || new FileInfo(path).Length != bytes)
            throw new PortalArtifactException(409, "El archivo de esta versión no coincide con el registro; no se puede publicar.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(sha256), Convert.FromHexString(digest)))
            throw new PortalArtifactException(409, "El archivo de esta versión fue modificado; no se puede publicar.");
    }
}

public sealed record ReceivedArtifact(string TemporaryPath, string Sha256, long Bytes);
