using Login.Domain.Saas;
using Microsoft.EntityFrameworkCore;

namespace Login.Data;

public partial class ApplicationDbContext
{
    public DbSet<SaasInvoice> SaasInvoices => Set<SaasInvoice>();
    public DbSet<SaasPayment> SaasPayments => Set<SaasPayment>();
    public DbSet<SaasSubscription> SaasSubscriptions => Set<SaasSubscription>();
    public DbSet<SaasPlanRequest> SaasPlanRequests => Set<SaasPlanRequest>();
    public DbSet<SaasAuditLog> SaasAuditLogs => Set<SaasAuditLog>();

    partial void ConfigureSaas(ModelBuilder b)
    {
        b.Entity<SaasInvoice>().HasAlternateKey(x => new { x.ClienteAppId, x.Id });
        b.Entity<SaasInvoice>().HasOne(x => x.ClienteApp).WithMany().HasForeignKey(x => x.ClienteAppId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaasInvoice>().HasIndex(x => new { x.ClienteAppId, x.IdempotencyKey }).IsUnique();
        b.Entity<SaasInvoice>().HasIndex(x => new { x.ClienteAppId, x.DueDate });
        b.Entity<SaasInvoice>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<SaasInvoice>().ToTable(t => t.HasCheckConstraint("CK_SaasInvoice_AmountPeriod", "[Amount] > 0 AND [PeriodEnd] >= [PeriodStart]"));
        b.Entity<SaasPayment>().HasOne(x => x.Invoice).WithMany(x => x.Payments)
            .HasForeignKey(x => new { x.ClienteAppId, x.InvoiceId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaasPayment>().HasIndex(x => new { x.ClienteAppId, x.IdempotencyKey }).IsUnique();
        b.Entity<SaasPayment>().HasIndex(x => new { x.ClienteAppId, x.ReferenceKey }).IsUnique();
        b.Entity<SaasPayment>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<SaasPayment>().ToTable(t => t.HasCheckConstraint("CK_SaasPayment_Amount", "[Amount] > 0"));
        b.Entity<SaasSubscription>().HasKey(x => x.ClienteAppId);
        b.Entity<SaasSubscription>().HasOne(x => x.ClienteApp).WithOne().HasForeignKey<SaasSubscription>(x => x.ClienteAppId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaasPlanRequest>().HasOne(x => x.ClienteApp).WithMany().HasForeignKey(x => x.ClienteAppId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaasPlanRequest>().HasOne(x => x.RequestedPlan).WithMany().HasForeignKey(x => x.RequestedPlanId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaasPlanRequest>().HasIndex(x => new { x.ClienteAppId, x.IdempotencyKey }).IsUnique();
        b.Entity<SaasPlanRequest>().HasIndex(x => x.ClienteAppId).IsUnique().HasFilter("[Status] = 'Pendiente'");
        b.Entity<SaasPlanRequest>().ToTable(t => t.HasCheckConstraint("CK_SaasPlanRequest_Status", "[Status] IN ('Pendiente','Aprobada','Rechazada')"));
        b.Entity<SaasAuditLog>().HasOne(x => x.ClienteApp).WithMany().HasForeignKey(x => x.ClienteAppId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaasAuditLog>().HasIndex(x => new { x.ClienteAppId, x.CreatedAt });
        b.Entity<SaasAuditLog>().HasIndex(x => new { x.ClienteAppId, x.Action, x.OperationKey }).IsUnique().HasFilter("[OperationKey] IS NOT NULL");
        foreach (var property in new[] { nameof(SaasInvoice.PeriodStart), nameof(SaasInvoice.PeriodEnd), nameof(SaasInvoice.DueDate) })
            b.Entity<SaasInvoice>().Property<DateTime>(property).HasColumnType("date");
        b.Entity<SaasPayment>().Property(x => x.PaidOn).HasColumnType("date");
        b.Entity<SaasSubscription>().Property(x => x.BaselineStart).HasColumnType("date");
        b.Entity<SaasSubscription>().Property(x => x.BaselineEnd).HasColumnType("date");
    }

    private void ProtectSaasAudit()
    {
        if (ChangeTracker.Entries<SaasAuditLog>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("La auditoría SaaS es de sólo adición.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectSaasAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ProtectSaasAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
