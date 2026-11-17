using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Envirapido.Portal.Api.Artifacts;

public sealed class PortalArtifactService(PortalDbContext db, PortalArtifactStorage storage,
    IApkInspector inspector, IOptionsMonitor<PortalArtifactOptions> options,
    IHostEnvironment environment, TimeProvider clock, PortalAuditService audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PortalArtifactListDto> ListAsync(CancellationToken cancellationToken)
    {
        var current = await CurrentIdAsync(cancellationToken);
        var items = await db.Artifacts.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return new(items.Select(x => Dto(x, current)).ToArray(), current, storage.IsConfigured, inspector.IsConfigured);
    }

    public async Task<PortalArtifactDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var artifact = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new PortalArtifactException(404, "Versión no encontrada.");
        return Dto(artifact, await CurrentIdAsync(cancellationToken));
    }

    public async Task<PortalArtifactDto> UploadAsync(ReceivedArtifact received, string originalName,
        string notes, string sourceCommit, string publicBaseUrl, string? expectedSha256,
        string actor, string operationId, CancellationToken cancellationToken)
    {
        ValidateMetadata(notes, sourceCommit, publicBaseUrl, expectedSha256);
        if (expectedSha256 is { Length: > 0 } && !received.Sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new PortalArtifactException(400, "El SHA-256 del archivo recibido no coincide con el indicado.");
        var operation = OperationKey(operationId);
        var fingerprint = Fingerprint(new { action = "upload", received.Sha256, notes, sourceCommit, publicBaseUrl, actor });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(operation, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        if (await db.Artifacts.AnyAsync(x => x.Sha256 == received.Sha256, cancellationToken))
            throw new PortalArtifactException(409, "Este mismo archivo ya está registrado. Usa su versión existente.");
        var artifact = new PortalArtifact
        {
            OriginalFileName = SafeName(originalName), Bytes = received.Bytes, Sha256 = received.Sha256,
            Notes = notes.Trim(), SourceCommit = sourceCommit.Trim().ToLowerInvariant(),
            PublicBaseUrl = "https://envirapido.com", CreatedAt = clock.GetUtcNow()
        };
        storage.FinalizeBlob(received);
        db.Artifacts.Add(artifact);
        var response = Dto(artifact, null);
        db.Set<PortalArtifactOperation>().Add(new() { Id = operation, Fingerprint = fingerprint,
            ResponseJson = JsonSerializer.Serialize(response, Json), CreatedAt = clock.GetUtcNow() });
        audit.Add(actor, "artifact.uploaded", "PortalArtifact", artifact.Id.ToString(), "Archivo privado en cuarentena. SHA-256: " + artifact.Sha256);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw new PortalArtifactException(409, "El archivo ya fue registrado por otra solicitud. Recarga la lista."); }
        await tx.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<PortalArtifactDto> ValidateAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var artifact = await db.Artifacts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new PortalArtifactException(404, "Versión no encontrada.");
        if (artifact.State == "Validated") return Dto(artifact, await CurrentIdAsync(cancellationToken));
        if (artifact.State is "Published" or "Withdrawn")
            throw new PortalArtifactException(409, "Una versión publicada o retirada no se modifica. Sube un archivo nuevo.");
        await storage.VerifyBlobAsync(artifact.Sha256, artifact.Bytes, cancellationToken);
        try
        {
            var inspected = await inspector.InspectAsync(storage.GetBlobPath(artifact.Sha256), cancellationToken);
            if (inspected.VersionCode < options.CurrentValue.EffectiveMinimumVersionCode(environment))
                throw new PortalArtifactException(422, "El versionCode está por debajo de la versión Android mínima admitida.");
            artifact.PackageName = inspected.PackageName; artifact.VersionCode = inspected.VersionCode;
            artifact.VersionName = inspected.VersionName; artifact.CertificateSha256 = inspected.CertificateSha256;
            artifact.ArchitecturesJson = JsonSerializer.Serialize(inspected.Architectures);
            artifact.MinSdk = inspected.MinSdk; artifact.TargetSdk = inspected.TargetSdk;
            artifact.State = "Validated"; artifact.ValidatedAt = clock.GetUtcNow();
            artifact.ValidationMessage = "Firma, paquete y versión comprobados. La URL declarada requiere la aprobación funcional antes de publicar.";
        }
        catch (PortalArtifactException failure) when (failure.StatusCode == 422)
        {
            artifact.State = "Rejected"; artifact.ValidatedAt = null;
            artifact.ValidationMessage = failure.Message;
        }
        artifact.Version = Guid.NewGuid().ToString("N");
        audit.Add(actor, "artifact.validated", "PortalArtifact", artifact.Id.ToString(), artifact.State + ": " + artifact.ValidationMessage);
        await SaveConcurrentAsync(cancellationToken);
        return Dto(artifact, await CurrentIdAsync(cancellationToken));
    }

    public async Task<PortalArtifactDto> PublishAsync(Guid id, PortalArtifactPublishRequest request,
        string actor, CancellationToken cancellationToken)
    {
        var operation = OperationKey(request.OperationId);
        var fingerprint = Fingerprint(new { action = "publish", id, request.ExpectedVersion, request.QaApproved, request.Reason, actor });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(operation, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        var artifact = await db.Artifacts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new PortalArtifactException(404, "Versión no encontrada.");
        RequireVersion(artifact, request.ExpectedVersion);
        var reason = Reason(request.Reason);
        if (!request.QaApproved) throw new PortalArtifactException(400, "Confirma que probaste esta versión y su conexión con envirapido.com antes de publicarla.");
        if (artifact.State != "Validated" || artifact.VersionCode is null || artifact.VersionName is null
            || artifact.PackageName != PortalArtifactOptions.PackageName || artifact.CertificateSha256 is null
            || !options.CurrentValue.IsCertificateAllowed(artifact.CertificateSha256))
            throw new PortalArtifactException(409, "Esta versión no tiene una validación Android aprobada para la firma autorizada.");
        var channel = await db.Set<PortalArtifactChannel>().SingleOrDefaultAsync(x => x.Id == "android-stable", cancellationToken);
        if (artifact.VersionCode < options.CurrentValue.EffectiveMinimumVersionCode(environment)
            || artifact.VersionCode <= (channel?.MaximumPublishedVersionCode ?? 0))
            throw new PortalArtifactException(409, "El versionCode debe ser mayor que todas las versiones ya publicadas.");
        await storage.VerifyBlobAsync(artifact.Sha256, artifact.Bytes, cancellationToken);
        if (channel is null)
        {
            channel = new PortalArtifactChannel(); db.Set<PortalArtifactChannel>().Add(channel);
        }
        channel.CurrentArtifactId = id; channel.MaximumPublishedVersionCode = artifact.VersionCode.Value;
        channel.Version = Guid.NewGuid().ToString("N");
        artifact.State = "Published"; artifact.PublishedAt = clock.GetUtcNow();
        artifact.Version = Guid.NewGuid().ToString("N");
        var response = Dto(artifact, id);
        db.Set<PortalArtifactOperation>().Add(new() { Id = operation, Fingerprint = fingerprint,
            ResponseJson = JsonSerializer.Serialize(response, Json), CreatedAt = clock.GetUtcNow() });
        audit.Add(actor, "artifact.published", "PortalArtifact", id.ToString(), reason + " | QA funcional confirmada por el operador; SHA-256: " + artifact.Sha256);
        await SaveConcurrentAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<PortalArtifactDto> WithdrawAsync(Guid id, PortalArtifactWithdrawRequest request,
        string actor, CancellationToken cancellationToken)
    {
        var operation = OperationKey(request.OperationId);
        var fingerprint = Fingerprint(new { action = "withdraw", id, request.ExpectedVersion, request.Reason, actor });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(operation, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        var artifact = await db.Artifacts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new PortalArtifactException(404, "Versión no encontrada.");
        RequireVersion(artifact, request.ExpectedVersion);
        var reason = Reason(request.Reason);
        if (artifact.State is not ("Published" or "Validated"))
            throw new PortalArtifactException(409, "La versión no está publicada ni validada para retirar.");
        var channel = await db.Set<PortalArtifactChannel>().SingleOrDefaultAsync(x => x.Id == "android-stable", cancellationToken);
        if (channel?.CurrentArtifactId == id)
        {
            channel.CurrentArtifactId = null; channel.Version = Guid.NewGuid().ToString("N");
        }
        artifact.State = "Withdrawn"; artifact.WithdrawnAt = clock.GetUtcNow();
        artifact.Version = Guid.NewGuid().ToString("N");
        var response = Dto(artifact, channel?.CurrentArtifactId);
        db.Set<PortalArtifactOperation>().Add(new() { Id = operation, Fingerprint = fingerprint,
            ResponseJson = JsonSerializer.Serialize(response, Json), CreatedAt = clock.GetUtcNow() });
        audit.Add(actor, "artifact.withdrawn", "PortalArtifact", id.ToString(), reason);
        await SaveConcurrentAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<PortalDownloadDto> CurrentDownloadAsync(CancellationToken cancellationToken)
    {
        var id = await CurrentIdAsync(cancellationToken);
        var artifact = id.HasValue ? await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Published", cancellationToken) : null;
        if (artifact?.PublishedAt is null || artifact.VersionCode is null || artifact.VersionName is null)
            throw new PortalArtifactException(404, "Aún no hay una versión privada publicada para descargar.");
        return new(artifact.Id, artifact.VersionName, artifact.VersionCode.Value, artifact.Bytes,
            artifact.Sha256, FileName(artifact.VersionCode.Value), $"/portal-api/v1/downloads/{artifact.Id}/file", artifact.PublishedAt.Value);
    }

    public async Task<(FileStream Stream, string FileName, DateTimeOffset PublishedAt, string Sha256)> OpenDownloadAsync(
        Guid id, Guid userId, bool isRange, CancellationToken cancellationToken)
    {
        var current = await CurrentDownloadAsync(cancellationToken);
        if (id != current.Id) throw new PortalArtifactException(404, "Esta versión ya no está disponible para descargar.");
        var path = storage.GetBlobPath(current.Sha256);
        FileStream file;
        try { file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new PortalArtifactException(503, "El archivo no está disponible temporalmente. Contacta con administración."); }
        try
        {
            if (file.Length != current.Bytes) throw new PortalArtifactException(503, "El archivo de la versión no coincide con su registro.");
            db.Set<PortalDownloadEvent>().Add(new() { UserId = userId, ArtifactId = id, At = clock.GetUtcNow(), RangeRequested = isRange });
            audit.Add(userId.ToString(), "artifact.download.authorized", "PortalArtifact", id.ToString(), isRange ? "Solicitud de rango autorizada." : "Descarga autorizada.");
            await db.SaveChangesAsync(cancellationToken);
            return (file, current.FileName, current.PublishedAt, current.Sha256);
        }
        catch { await file.DisposeAsync(); throw; }
    }

    public Task<Guid?> CurrentIdAsync(CancellationToken cancellationToken) => db.Set<PortalArtifactChannel>().AsNoTracking()
        .Where(x => x.Id == "android-stable").Select(x => x.CurrentArtifactId).SingleOrDefaultAsync(cancellationToken);

    private static string FileName(int code) => $"Envirapido-android-{code}-release.apk";
    private static string SafeName(string input)
    {
        var name = Path.GetFileName(input.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        return name.Length <= 160 ? name : name[..160];
    }

    public static void ValidateMetadata(string notes, string sourceCommit, string publicBaseUrl, string? expectedSha256)
    {
        if (notes is null || notes.Length > 4000 || sourceCommit is null
            || !Regex.IsMatch(sourceCommit.Trim(), "^[a-fA-F0-9]{40}$"))
            throw new PortalArtifactException(400, "Indica el commit completo del build y notas de hasta 4000 caracteres.");
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https"
            || !origin.Host.Equals("envirapido.com", StringComparison.OrdinalIgnoreCase) || !origin.IsDefaultPort
            || origin.UserInfo.Length > 0 || origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0)
            throw new PortalArtifactException(400, "La URL declarada del build debe ser https://envirapido.com.");
        if (!string.IsNullOrEmpty(expectedSha256) && !Regex.IsMatch(expectedSha256, "^[a-fA-F0-9]{64}$"))
            throw new PortalArtifactException(400, "El SHA-256 esperado debe tener 64 caracteres hexadecimales.");
    }

    private async Task SaveConcurrentAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw new PortalArtifactException(409, "La versión cambió o esta operación ya fue registrada. Recarga antes de continuar."); }
    }

    private static void RequireVersion(PortalArtifact artifact, string version)
    {
        if (string.IsNullOrEmpty(version) || !artifact.Version.Equals(version, StringComparison.Ordinal))
            throw new PortalArtifactException(409, "La versión cambió desde que abriste esta página. Recarga antes de continuar.");
    }

    private static string Reason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500)
            throw new PortalArtifactException(400, "Indica un motivo entre 3 y 500 caracteres.");
        return reason.Trim();
    }

    private static string OperationKey(string key) => Guid.TryParse(key, out var parsed)
        ? parsed.ToString("N") : throw new PortalArtifactException(400, "La operación necesita un identificador válido.");
    private static string Fingerprint<T>(T input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, Json)))).ToLowerInvariant();

    private async Task<PortalArtifactDto?> ReplayAsync(string operation, string fingerprint, CancellationToken cancellationToken)
    {
        var previous = await db.Set<PortalArtifactOperation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == operation, cancellationToken);
        if (previous is null) return null;
        if (previous.Fingerprint != fingerprint) throw new PortalArtifactException(409, "El identificador ya se utilizó con otros datos.");
        return JsonSerializer.Deserialize<PortalArtifactDto>(previous.ResponseJson, Json)
            ?? throw new PortalArtifactException(409, "No se pudo recuperar la operación anterior.");
    }

    private static PortalArtifactDto Dto(PortalArtifact artifact, Guid? current) => new(artifact.Id,
        artifact.OriginalFileName, artifact.State, artifact.VersionName, artifact.VersionCode, artifact.Bytes,
        artifact.Sha256, artifact.PackageName, artifact.CertificateSha256,
        JsonSerializer.Deserialize<string[]>(artifact.ArchitecturesJson) ?? [], artifact.MinSdk, artifact.TargetSdk,
        artifact.Notes, artifact.SourceCommit, artifact.PublicBaseUrl, artifact.CreatedAt, artifact.ValidatedAt,
        artifact.PublishedAt, artifact.WithdrawnAt, artifact.ValidationMessage, current == artifact.Id, artifact.Version);
}
