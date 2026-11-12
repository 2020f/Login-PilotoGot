using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Data;

public partial class ApplicationDbContext
{
    public DbSet<DevolucionOperativa> DevolucionesOperativas => Set<DevolucionOperativa>();
    partial void ConfigureDevoluciones(ModelBuilder b)
    {
        ConfigureTenant<DevolucionOperativa>(b);
        b.Entity<DevolucionOperativa>().HasOne(x => x.OrdenEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId })
            .HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DevolucionOperativa>().HasOne(x => x.ParadaEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId })
            .HasPrincipalKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DevolucionOperativa>().HasOne(x => x.Piloto).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.PilotoId })
            .HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DevolucionOperativa>().HasIndex(x => new { x.ClienteAppId, x.ParadaEntregaId })
            .IsUnique().HasFilter("[Abierta] = 1");
        b.Entity<DevolucionOperativa>().HasOne<Domain.Entities.Tienda>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.TiendaRecepcionId })
            .HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
