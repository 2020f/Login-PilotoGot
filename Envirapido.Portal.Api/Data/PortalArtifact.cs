using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Envirapido.Portal.Api.Data;

public sealed class PortalArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalFileName { get; set; } = "";
    public string State { get; set; } = "Quarantined";
    public string? VersionName { get; set; }
    public int? VersionCode { get; set; }
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string? PackageName { get; set; }
    public string? CertificateSha256 { get; set; }
    public string ArchitecturesJson { get; set; } = "[]";
    public int? MinSdk { get; set; }
    public int? TargetSdk { get; set; }
    public string Notes { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public string? ValidationMessage { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
}

// The release pointer and high-water mark are private to PortalDB. Withdrawing
// a release must not allow a previously published versionCode to be reused.
public sealed class PortalArtifactChannel
{
    public string Id { get; set; } = "android-stable";
    public Guid? CurrentArtifactId { get; set; }
    public int MaximumPublishedVersionCode { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class PortalArtifactOperation
{
    public string Id { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PortalDownloadEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ArtifactId { get; set; }
    public DateTimeOffset At { get; set; }
    public bool RangeRequested { get; set; }
}

public static class PortalArtifactModel
{
    public static void Configure(ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTimeOffset, DateTime>(value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Entity<PortalArtifact>(e =>
        {
            e.ToTable("PortalArtifacts"); e.HasKey(x => x.Id);
            e.Property(x => x.OriginalFileName).HasMaxLength(160);
            e.Property(x => x.State).HasMaxLength(24);
            e.Property(x => x.VersionName).HasMaxLength(80);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.Sha256).IsUnique();
            e.Property(x => x.PackageName).HasMaxLength(160);
            e.Property(x => x.CertificateSha256).HasMaxLength(64);
            e.Property(x => x.ArchitecturesJson).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.SourceCommit).HasMaxLength(40);
            e.Property(x => x.PublicBaseUrl).HasMaxLength(200);
            e.Property(x => x.ValidationMessage).HasMaxLength(500);
            e.Property(x => x.Version).HasMaxLength(32).IsConcurrencyToken();
            e.Property(x => x.CreatedAt).HasConversion(utc);
            e.Property(x => x.ValidatedAt).HasConversion(utc);
            e.Property(x => x.PublishedAt).HasConversion(utc);
            e.Property(x => x.WithdrawnAt).HasConversion(utc);
        });
        builder.Entity<PortalArtifactChannel>(e =>
        {
            e.ToTable("PortalArtifactChannels"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.Version).HasMaxLength(32).IsConcurrencyToken();
            e.HasOne<PortalArtifact>().WithMany().HasForeignKey(x => x.CurrentArtifactId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PortalArtifactOperation>(e =>
        {
            e.ToTable("PortalArtifactOperations"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.ResponseJson).HasMaxLength(16000);
            e.Property(x => x.CreatedAt).HasConversion(utc);
        });
        builder.Entity<PortalDownloadEvent>(e =>
        {
            e.ToTable("PortalDownloadEvents"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.At });
            e.Property(x => x.At).HasConversion(utc);
            e.HasOne<PortalArtifact>().WithMany().HasForeignKey(x => x.ArtifactId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
