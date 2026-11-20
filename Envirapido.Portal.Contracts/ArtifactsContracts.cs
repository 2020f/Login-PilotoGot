namespace Envirapido.Portal.Contracts;

public sealed record PortalArtifactDto(
    Guid Id, string OriginalFileName, string State, string? VersionName, int? VersionCode,
    long Bytes, string Sha256, string? PackageName, string? CertificateSha256,
    IReadOnlyList<string> Architectures, int? MinSdk, int? TargetSdk, string Notes,
    string SourceCommit, string PublicBaseUrl, DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt, DateTimeOffset? PublishedAt, DateTimeOffset? WithdrawnAt,
    string? ValidationMessage, bool IsCurrent, string Version);

public sealed record PortalArtifactListDto(IReadOnlyList<PortalArtifactDto> Items,
    Guid? CurrentArtifactId, bool StorageConfigured, bool ValidationToolsConfigured);

public sealed record PortalArtifactPublishRequest(string OperationId, string ExpectedVersion,
    bool QaApproved, string Reason);

public sealed record PortalArtifactWithdrawRequest(string OperationId, string ExpectedVersion,
    string Reason);

public sealed record PortalDownloadDto(Guid Id, string VersionName, int VersionCode,
    long Bytes, string Sha256, string FileName, string DownloadPath, DateTimeOffset PublishedAt);
