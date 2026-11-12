using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Data;

public partial class ApplicationDbContext
{
    public DbSet<ZonaOperativa> ZonasOperativas => Set<ZonaOperativa>();
    public DbSet<RutaOperativa> RutasOperativas => Set<RutaOperativa>();
    public DbSet<RutaOrdenOperativa> RutaOrdenesOperativas => Set<RutaOrdenOperativa>();
    public DbSet<PuntoRutaOperativa> PuntosRutaOperativos => Set<PuntoRutaOperativa>();
    public DbSet<IncidenciaOperativa> IncidenciasOperativas => Set<IncidenciaOperativa>();
    public DbSet<ActividadIncidenciaOperativa> ActividadesIncidencia => Set<ActividadIncidenciaOperativa>();
    public DbSet<ReglaIncidenciaOperativa> ReglasIncidencia => Set<ReglaIncidenciaOperativa>();
    public DbSet<ReglaRemuneracionOperativa> ReglasRemuneracion => Set<ReglaRemuneracionOperativa>();
    public DbSet<LiquidacionOperativa> LiquidacionesOperativas => Set<LiquidacionOperativa>();
    public DbSet<MovimientoOperativo> MovimientosOperativos => Set<MovimientoOperativo>();
    public DbSet<CobroOperativo> CobrosOperativos => Set<CobroOperativo>();
    public DbSet<DatosEnvioOperativos> DatosEnviosOperativos => Set<DatosEnvioOperativos>();
    public DbSet<OperacionEnvirapido> OperacionesEnvirapido => Set<OperacionEnvirapido>();
    public DbSet<EventoOperativo> EventosOperativos => Set<EventoOperativo>();
    public DbSet<PosicionPilotoOperativa> PosicionesPilotosOperativas => Set<PosicionPilotoOperativa>();

    partial void ConfigureOperativos(ModelBuilder b)
    {
        ConfigureTenant<ZonaOperativa>(b); ConfigureTenant<RutaOperativa>(b);
        ConfigureTenant<IncidenciaOperativa>(b); ConfigureTenant<ReglaIncidenciaOperativa>(b);
        ConfigureTenant<ReglaRemuneracionOperativa>(b); ConfigureTenant<LiquidacionOperativa>(b);
        ConfigureTenant<MovimientoOperativo>(b); ConfigureTenant<CobroOperativo>(b);
        ConfigureTenant<DatosEnvioOperativos>(b);

        b.Entity<ZonaOperativa>().HasIndex(x => new { x.ClienteAppId, x.Nombre }).IsUnique();
        b.Entity<RutaOperativa>().HasOne(x => x.Zona).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.ZonaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RutaOperativa>().HasOne(x => x.Piloto).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.PilotoId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RutaOrdenOperativa>().HasAlternateKey(x => new { x.ClienteAppId, x.RutaId, x.OrdenEntregaId });
        b.Entity<RutaOrdenOperativa>().HasIndex(x => new { x.ClienteAppId, x.OrdenEntregaId }).IsUnique().HasFilter("[Vigente] = 1");
        b.Entity<RutaOrdenOperativa>().HasOne(x => x.Ruta).WithMany(x => x.Ordenes)
            .HasForeignKey(x => new { x.ClienteAppId, x.RutaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RutaOrdenOperativa>().HasOne(x => x.OrdenEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PuntoRutaOperativa>().HasOne(x => x.Ruta).WithMany(x => x.Puntos)
            .HasForeignKey(x => new { x.ClienteAppId, x.RutaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PuntoRutaOperativa>().HasOne(x => x.RutaOrden).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.RutaId, x.OrdenEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.RutaId, x.OrdenEntregaId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PuntoRutaOperativa>().HasOne(x => x.ParadaEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PuntoRutaOperativa>().HasIndex(x => new { x.RutaId, x.Posicion }).IsUnique();
        b.Entity<PuntoRutaOperativa>().ToTable(t => t.HasCheckConstraint("CK_PuntoRuta_Tipo", "([Tipo] = 1 AND [ParadaEntregaId] IS NULL) OR ([Tipo] IN (2, 3) AND [ParadaEntregaId] IS NOT NULL)"));

        b.Entity<IncidenciaOperativa>().HasOne(x => x.OrdenEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<IncidenciaOperativa>().HasOne(x => x.ParadaEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<IncidenciaOperativa>().HasOne(x => x.Ruta).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.RutaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<IncidenciaOperativa>().HasIndex(x => new { x.ClienteAppId, x.ClaveDeteccion }).IsUnique().HasFilter("[ClaveDeteccion] IS NOT NULL");
        b.Entity<IncidenciaOperativa>().ToTable(t => t.HasCheckConstraint("CK_Incidencia_Ambito", "([OrdenEntregaId] IS NOT NULL OR [RutaId] IS NOT NULL) AND ([ParadaEntregaId] IS NULL OR [OrdenEntregaId] IS NOT NULL)"));
        b.Entity<ActividadIncidenciaOperativa>().HasOne(x => x.Incidencia).WithMany(x => x.Actividad)
            .HasForeignKey(x => new { x.ClienteAppId, x.IncidenciaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReglaIncidenciaOperativa>().HasIndex(x => new { x.ClienteAppId, x.Categoria }).IsUnique();
        b.Entity<LiquidacionOperativa>().HasOne(x => x.Piloto).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.PilotoId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LiquidacionOperativa>().HasOne(x => x.Tienda).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.TiendaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LiquidacionOperativa>().ToTable(t => t.HasCheckConstraint("CK_Liquidacion_Beneficiario", "([PilotoId] IS NOT NULL AND [TiendaId] IS NULL) OR ([PilotoId] IS NULL AND [TiendaId] IS NOT NULL)"));
        b.Entity<MovimientoOperativo>().HasOne(x => x.Liquidacion).WithMany(x => x.Movimientos)
            .HasForeignKey(x => new { x.ClienteAppId, x.LiquidacionId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MovimientoOperativo>().HasIndex(x => new { x.ClienteAppId, x.ClaveOrigen }).IsUnique();
        b.Entity<MovimientoOperativo>().HasOne<Domain.Entities.OrdenEntrega>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MovimientoOperativo>().HasOne<Domain.Entities.ParadaEntrega>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MovimientoOperativo>().HasOne<RutaOperativa>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.RutaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MovimientoOperativo>().HasOne<ReglaRemuneracionOperativa>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.ReglaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MovimientoOperativo>().ToTable(t => t.HasCheckConstraint("CK_Movimiento_Parada", "[ParadaEntregaId] IS NULL OR [OrdenEntregaId] IS NOT NULL"));
        b.Entity<CobroOperativo>().HasOne(x => x.OrdenEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CobroOperativo>().HasOne(x => x.Piloto).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.PilotoId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CobroOperativo>().HasOne(x => x.Tienda).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.TiendaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CobroOperativo>().HasIndex(x => new { x.ClienteAppId, x.Referencia }).IsUnique();
        b.Entity<CobroOperativo>().HasOne<Domain.Entities.ParadaEntrega>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.ParadaEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.OrdenEntregaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CobroOperativo>().HasOne<LiquidacionOperativa>().WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.LiquidacionId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DatosEnvioOperativos>().HasOne(x => x.OrdenEntrega).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.OrdenEntregaId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DatosEnvioOperativos>().HasIndex(x => new { x.ClienteAppId, x.OrdenEntregaId }).IsUnique();
        b.Entity<OperacionEnvirapido>().HasIndex(x => new { x.ClienteAppId, x.ActorId, x.Tipo, x.Clave }).IsUnique();
        b.Entity<OperacionEnvirapido>().HasOne<Domain.Entities.ClienteApp>().WithMany().HasForeignKey(x => x.ClienteAppId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EventoOperativo>().HasOne<Domain.Entities.ClienteApp>().WithMany().HasForeignKey(x => x.ClienteAppId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PosicionPilotoOperativa>().HasOne(x => x.Piloto).WithMany()
            .HasForeignKey(x => new { x.ClienteAppId, x.PilotoId }).HasPrincipalKey(x => new { x.ClienteAppId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PosicionPilotoOperativa>().HasIndex(x => new { x.ClienteAppId, x.PilotoId, x.DispositivoId, x.Secuencia }).IsUnique();
        b.Entity<PosicionPilotoOperativa>().HasIndex(x => new { x.ClienteAppId, x.PilotoId, x.CapturadoAt });
        foreach (var type in new[] { typeof(LiquidacionOperativa), typeof(MovimientoOperativo), typeof(CobroOperativo), typeof(ReglaRemuneracionOperativa) })
            foreach (var p in b.Model.FindEntityType(type)!.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            { p.SetPrecision(18); p.SetScale(2); }
    }

    private static void ConfigureTenant<T>(ModelBuilder b) where T : OperativoTenant
    {
        // Explicit tables avoid an inheritance discriminator joining unrelated aggregates.
        b.Entity<T>().HasBaseType((Type?)null);
        b.Entity<T>().HasKey(x => x.Id);
        b.Entity<T>().HasAlternateKey(x => new { x.ClienteAppId, x.Id });
        b.Entity<T>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<T>().HasOne(x => x.ClienteApp).WithMany().HasForeignKey(x => x.ClienteAppId).OnDelete(DeleteBehavior.Restrict);
    }
}
