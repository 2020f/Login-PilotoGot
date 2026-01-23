using Login.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Login.Data
{
    public class ApplicationDbContext
        : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Plan> Planes => Set<Plan>();
        public DbSet<ClienteApp> ClientesApp => Set<ClienteApp>();
        public DbSet<Tienda> Tiendas => Set<Tienda>();
        public DbSet<UsuarioFinal> UsuariosFinales => Set<UsuarioFinal>();
        public DbSet<Piloto> Pilotos => Set<Piloto>();
        public DbSet<OrdenEntrega> OrdenesEntrega => Set<OrdenEntrega>();
        public DbSet<CodigoEntrega> CodigosEntrega => Set<CodigoEntrega>();
        public DbSet<HistorialOrden> HistorialOrdenes => Set<HistorialOrden>();
        public DbSet<UsuarioTienda> UsuariosTienda { get; set; } = null!;
        public DbSet<UserContext> UserContexts { get; set; } = null!;
        public DbSet<UsuarioClienteApp> UsuariosClienteApp { get; set; } = null!;




        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ✅ A único por tenant
            builder.Entity<OrdenEntrega>()
                .HasIndex(o => new { o.ClienteAppId, o.NumeroOrdenA })
                .IsUnique();

            // ✅ Código único global
            builder.Entity<CodigoEntrega>()
                .HasIndex(c => c.Codigo)
                .IsUnique();

            // ✅ Una orden solo tiene 1 B y 1 C
            builder.Entity<CodigoEntrega>()
                .HasIndex(c => new { c.OrdenEntregaId, c.Tipo })
                .IsUnique();

            // Relaciones con restricciones (evita cascadas peligrosas)
            builder.Entity<OrdenEntrega>()
                .HasOne(o => o.Piloto)
                .WithMany(p => p.Ordenes)
                .HasForeignKey(o => o.PilotoId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<OrdenEntrega>()
                .HasOne(o => o.Tienda)
                .WithMany(t => t.Ordenes)
                .HasForeignKey(o => o.TiendaId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<OrdenEntrega>()
                .HasOne(o => o.UsuarioFinal)
                .WithMany(u => u.Ordenes)
                .HasForeignKey(o => o.UsuarioFinalId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Tienda>()
                .HasOne(t => t.ClienteApp)
                .WithMany(c => c.Tiendas)
                .HasForeignKey(t => t.ClienteAppId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Piloto>()
                .HasOne(p => p.ClienteApp)
                .WithMany(c => c.Pilotos)
                .HasForeignKey(p => p.ClienteAppId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<UsuarioTienda>()
                 .HasIndex(x => new { x.IdentityUserId, x.TiendaId })
                 .IsUnique();

            builder.Entity<UsuarioTienda>()
                .HasOne(x => x.Tienda)
                .WithMany() // si quieres colección en Tienda, luego lo cambiamos
                .HasForeignKey(x => x.TiendaId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserContext>()
                .HasIndex(x => x.IdentityUserId)
                .IsUnique();



            builder.Entity<UsuarioClienteApp>()
               .HasIndex(x => new { x.IdentityUserId, x.ClienteAppId })
                .IsUnique();

            builder.Entity<UsuarioClienteApp>()
                .HasOne(x => x.ClienteApp)
                .WithMany()
                .HasForeignKey(x => x.ClienteAppId)
                .OnDelete(DeleteBehavior.Cascade);




        }
    }
}
