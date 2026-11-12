using Login.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Login.Data
{
    public partial class ApplicationDbContext
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
        public DbSet<OrdenAutoRechazo> OrdenesAutoRechazo => Set<OrdenAutoRechazo>();
        public DbSet<OperacionOrden> OperacionesOrden => Set<OperacionOrden>();
        public DbSet<ParadaEntrega> ParadasEntrega => Set<ParadaEntrega>();
        partial void ConfigureOperativos(ModelBuilder b);
        partial void ConfigureDevoluciones(ModelBuilder b);
        partial void ConfigureSaas(ModelBuilder b);




        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<OperacionOrden>()
                .HasIndex(x => new { x.ClienteAppId, x.ActorIdentityUserId, x.Tipo, x.Clave }).IsUnique();
            builder.Entity<OperacionOrden>().HasOne(x => x.ClienteApp).WithMany()
                .HasForeignKey(x => x.ClienteAppId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<OperacionOrden>().HasOne(x => x.OrdenEntrega).WithMany()
                .HasForeignKey(x => x.OrdenEntregaId).OnDelete(DeleteBehavior.Restrict);

            // ✅ Precisión para montos (evita truncados)
            builder.Entity<ClienteApp>()
                .Property(c => c.PrecioBase).HasPrecision(18, 2);
            builder.Entity<ClienteApp>()
                .Property(c => c.PrecioPorKm).HasPrecision(18, 2);
            builder.Entity<OrdenEntrega>()
                .Property(o => o.Precio).HasPrecision(18, 2);

            // ✅ A único por tenant
            builder.Entity<OrdenEntrega>()
                .HasIndex(o => new { o.ClienteAppId, o.NumeroOrdenA })
                .IsUnique();

            // Los cuatro dígitos se reservan por tenant mientras la orden esté abierta.
            builder.Entity<OrdenEntrega>()
                .HasIndex(o => new { o.ClienteAppId, o.CodigoA })
                .IsUnique()
                .HasFilter("[CodigoA] IS NOT NULL AND [Estado] <> 4 AND [Estado] <> 6 AND [Estado] <> 7 AND [Estado] <> 8");

            builder.Entity<CodigoEntrega>()
                .HasIndex(c => c.Codigo);
            builder.Entity<CodigoEntrega>()
                .HasIndex(c => new { c.ClienteAppId, c.Tipo, c.Codigo })
                .IsUnique()
                .HasFilter("[Reservado] = 1");
            builder.Entity<CodigoEntrega>()
                .HasOne(c => c.ClienteApp)
                .WithMany()
                .HasForeignKey(c => c.ClienteAppId)
                .OnDelete(DeleteBehavior.Restrict);

            // ✅ Una orden solo tiene 1 B y 1 C
            builder.Entity<CodigoEntrega>().HasIndex(c => new { c.OrdenEntregaId, c.Tipo })
                .IsUnique().HasFilter("[ParadaEntregaId] IS NULL");
            builder.Entity<CodigoEntrega>().HasIndex(c => c.ParadaEntregaId)
                .IsUnique().HasFilter("[ParadaEntregaId] IS NOT NULL");
            builder.Entity<CodigoEntrega>().ToTable(t => t.HasCheckConstraint("CK_CodigoEntrega_ParadaTipo",
                "([Tipo] = 1 AND [ParadaEntregaId] IS NULL) OR [Tipo] = 2"));
            builder.Entity<OrdenEntrega>().HasAlternateKey(o => new { o.ClienteAppId, o.Id, o.TiendaId });
            builder.Entity<OrdenEntrega>().HasAlternateKey(o => new { o.ClienteAppId, o.Id });
            builder.Entity<Tienda>().HasAlternateKey(t => new { t.ClienteAppId, t.Id });
            builder.Entity<Piloto>().HasAlternateKey(p => new { p.ClienteAppId, p.Id });
            builder.Entity<UsuarioFinal>().HasAlternateKey(u => new { u.TiendaId, u.Id });
            builder.Entity<ParadaEntrega>().HasAlternateKey(p => new { p.ClienteAppId, p.OrdenEntregaId, p.Id });
            builder.Entity<ParadaEntrega>().HasIndex(p => new { p.OrdenEntregaId, p.Posicion }).IsUnique();
            builder.Entity<ParadaEntrega>().HasOne(p => p.OrdenEntrega).WithMany(o => o.Paradas)
                .HasForeignKey(p => new { p.ClienteAppId, p.OrdenEntregaId, p.TiendaId })
                .HasPrincipalKey(o => new { o.ClienteAppId, o.Id, o.TiendaId }).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ParadaEntrega>().HasOne(p => p.UsuarioFinal).WithMany()
                .HasForeignKey(p => new { p.TiendaId, p.UsuarioFinalId })
                .HasPrincipalKey(u => new { u.TiendaId, u.Id }).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<CodigoEntrega>().HasOne(c => c.OrdenEntrega).WithMany(o => o.Codigos)
                .HasForeignKey(c => new { c.ClienteAppId, c.OrdenEntregaId })
                .HasPrincipalKey(o => new { o.ClienteAppId, o.Id }).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<CodigoEntrega>().HasOne(c => c.ParadaEntrega).WithMany(p => p.Codigos)
                .HasForeignKey(c => new { c.ClienteAppId, c.OrdenEntregaId, c.ParadaEntregaId })
                .HasPrincipalKey(p => new { p.ClienteAppId, p.OrdenEntregaId, p.Id }).OnDelete(DeleteBehavior.Restrict);

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

            // ✅ Un piloto rechaza una orden auto una sola vez
            builder.Entity<OrdenAutoRechazo>()
                .HasIndex(x => new { x.OrdenEntregaId, x.PilotoId })
                .IsUnique();

            builder.Entity<OrdenAutoRechazo>()
                .HasOne(x => x.OrdenEntrega)
                .WithMany()
                .HasForeignKey(x => x.OrdenEntregaId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<OrdenAutoRechazo>()
                .HasOne(x => x.Piloto)
                .WithMany()
                .HasForeignKey(x => x.PilotoId)
                .OnDelete(DeleteBehavior.Restrict);




            ConfigureOperativos(builder);
            ConfigureDevoluciones(builder);
            ConfigureSaas(builder);
        }
    }
}
