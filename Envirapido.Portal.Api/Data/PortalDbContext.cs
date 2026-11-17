using Microsoft.EntityFrameworkCore;

namespace Envirapido.Portal.Api.Data;

public sealed partial class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder b) => b.Properties<DateTimeOffset>().HaveConversion<PortalUtcTicksConverter>();
    public DbSet<PortalUser> Users => Set<PortalUser>();
    public DbSet<PortalProfile> Profiles => Set<PortalProfile>();
    public DbSet<PortalSession> Sessions => Set<PortalSession>();
    public DbSet<PortalToken> Tokens => Set<PortalToken>();
    public DbSet<PortalCatalogEntry> CatalogEntries => Set<PortalCatalogEntry>();
    public DbSet<PortalPurchaseRequest> Requests => Set<PortalPurchaseRequest>();
    public DbSet<PortalPaymentReport> PaymentReports => Set<PortalPaymentReport>();
    public DbSet<PortalPayment> Payments => Set<PortalPayment>();
    public DbSet<PortalApproval> Approvals => Set<PortalApproval>();
    public DbSet<PortalAccessGrant> AccessGrants => Set<PortalAccessGrant>();
    public DbSet<PortalAuditEvent> AuditEvents => Set<PortalAuditEvent>();
    public DbSet<PortalOperation> Operations => Set<PortalOperation>();
    public DbSet<PortalOutbox> Outbox => Set<PortalOutbox>();
    public DbSet<PortalArtifact> Artifacts => Set<PortalArtifact>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<PortalUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Entity<PortalUser>().Property(x => x.Email).HasMaxLength(256);
        b.Entity<PortalUser>().Property(x => x.NormalizedEmail).HasMaxLength(256);
        b.Entity<PortalUser>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<PortalProfile>().HasKey(x => x.UserId);
        b.Entity<PortalProfile>().HasOne(x => x.User).WithOne().HasForeignKey<PortalProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalSession>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalSession>().HasIndex(x => new { x.UserId, x.ExpiresAt });
        b.Entity<PortalToken>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalToken>().HasIndex(x => new { x.UserId, x.Purpose, x.Hash }).IsUnique();
        b.Entity<PortalToken>().Property(x => x.Purpose).HasMaxLength(40); b.Entity<PortalToken>().Property(x => x.Hash).HasMaxLength(64);
        b.Entity<PortalCatalogEntry>().HasKey(x => x.Code);
        b.Entity<PortalCatalogEntry>().Property(x => x.Code).HasMaxLength(80);
        b.Entity<PortalCatalogEntry>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<PortalPurchaseRequest>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalPurchaseRequest>().HasAlternateKey(x => new { x.UserId, x.Id });
        b.Entity<PortalPurchaseRequest>().HasIndex(x => x.UserId).IsUnique().HasFilter("[Status] IN ('Submitted','InReview','NeedsInformation')");
        b.Entity<PortalPurchaseRequest>().Property(x => x.Status).HasMaxLength(30);
        b.Entity<PortalPurchaseRequest>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<PortalPurchaseRequest>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<PortalPaymentReport>().HasOne(x => x.Request).WithMany(x => x.PaymentReports).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalPaymentReport>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<PortalPaymentReport>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<PortalPaymentReport>().Property(x => x.Status).HasMaxLength(30);
        b.Entity<PortalPaymentReport>().HasAlternateKey(x => new { x.RequestId, x.Id });
        b.Entity<PortalPayment>().HasOne(x => x.Request).WithMany(x => x.Payments).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalPayment>().HasOne<PortalPaymentReport>().WithMany().HasForeignKey(x => new { x.RequestId, x.ReportId }).HasPrincipalKey(x => new { x.RequestId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalPayment>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<PortalPayment>().Property(x => x.ReferenceKey).HasMaxLength(140);
        b.Entity<PortalPayment>().HasIndex(x => x.ReferenceKey).IsUnique();
        b.Entity<PortalPayment>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<PortalApproval>().HasOne(x => x.Request).WithMany(x => x.Decisions).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalAccessGrant>().HasKey(x => x.UserId);
        b.Entity<PortalAccessGrant>().HasOne(x => x.User).WithOne().HasForeignKey<PortalAccessGrant>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalAccessGrant>().HasOne(x => x.Request).WithMany().HasForeignKey(x => new { x.UserId, x.RequestId }).HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PortalAccessGrant>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<PortalOperation>().Property(x => x.Scope).HasMaxLength(300);
        b.Entity<PortalOperation>().Property(x => x.Key).HasMaxLength(40);
        b.Entity<PortalOperation>().HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        b.Entity<PortalAuditEvent>().Property(x => x.Actor).HasMaxLength(150);
        b.Entity<PortalAuditEvent>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<PortalAuditEvent>().Property(x => x.Entity).HasMaxLength(80);
        b.Entity<PortalAuditEvent>().Property(x => x.EntityId).HasMaxLength(100);
        b.Entity<PortalAuditEvent>().Property(x => x.Detail).HasMaxLength(2000);
        b.Entity<PortalOutbox>().HasIndex(x => new { x.SentAt, x.NextAttemptAt });
        PortalArtifactModel.Configure(b);
    }
    private void ProtectAudit()
    {
        if (ChangeTracker.Entries<PortalAuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PortalApproval>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("La auditoría y decisiones del portal son de solo adición.");
        if (ChangeTracker.Entries<PortalPurchaseRequest>().Any(x => x.State == EntityState.Modified &&
            (x.Property(p => p.PlanSnapshotJson).IsModified || x.Property(p => p.ProfileSnapshotJson).IsModified || x.Property(p => p.Amount).IsModified || x.Property(p => p.Currency).IsModified || x.Property(p => p.UserId).IsModified)))
            throw new InvalidOperationException("Las condiciones y titular de una solicitud emitida son inmutables.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ProtectAudit(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default) { ProtectAudit(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
}
