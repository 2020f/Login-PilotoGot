using Login.Domain.Entities;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Data;

public partial class ApplicationDbContext
{
    public DbSet<RetiroParadaOperativa> RetirosParadas => Set<RetiroParadaOperativa>();
    partial void ConfigureReasignaciones(ModelBuilder b)
    {
        ConfigureTenant<RetiroParadaOperativa>(b);
        b.Entity<ParadaEntrega>().Property(p => p.RevisionAsignacion).IsConcurrencyToken();
        b.Entity<ParadaEntrega>().HasOne<Piloto>().WithMany()
            .HasForeignKey(p => new { p.ClienteAppId, p.PilotoAsignadoId }).HasPrincipalKey(p => new { p.ClienteAppId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ParadaEntrega>().HasOne<Piloto>().WithMany()
            .HasForeignKey(p => new { p.ClienteAppId, p.PilotoCustodioId }).HasPrincipalKey(p => new { p.ClienteAppId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ParadaEntrega>().HasOne<Piloto>().WithMany()
            .HasForeignKey(p => new { p.ClienteAppId, p.PilotoEntregaId }).HasPrincipalKey(p => new { p.ClienteAppId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RetiroParadaOperativa>().HasOne(x => x.OrdenEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RetiroParadaOperativa>().HasOne(x => x.ParadaEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId })
            .HasPrincipalKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RetiroParadaOperativa>().HasOne<Piloto>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.PilotoOrigenId }).HasPrincipalKey(p => new { p.ClienteAppId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RetiroParadaOperativa>().HasOne<Piloto>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.PilotoDestinoId }).HasPrincipalKey(p => new { p.ClienteAppId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RetiroParadaOperativa>().HasOne<Tienda>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.TiendaDestinoId }).HasPrincipalKey(p => new { p.ClienteAppId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RetiroParadaOperativa>().HasOne<IncidenciaOperativa>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.IncidenciaId }).HasPrincipalKey(p => new { p.ClienteAppId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RetiroParadaOperativa>().HasIndex(x => new { x.ClienteAppId, x.ParadaEntregaId }).IsUnique().HasFilter("[Abierta] = 1");
        b.Entity<RetiroParadaOperativa>().ToTable(t => t.HasCheckConstraint("CK_Retiro_Estado",
            "[Estado] IN ('Solicitada','Rechazada','PorReasignar','PendienteRecepcion','Recibida','Reasignada','Cancelada')"));
    }
}
