using Login.Domain.Entities;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed record ResumenLiquidacionesEstado(int Cantidad, decimal Neto);
public sealed record ResumenCobrosPeriodo(int Cantidad, decimal ImporteRegistrado,
    int Conciliados, decimal ImporteVerificado, int RecibidosEmpresa, decimal ImporteRecibidoEmpresa,
    int PendientesRecepcion, decimal ImportePendienteRecepcion, int ConDiferencias, decimal DiferenciasAbsolutas);
public sealed record IndicadoresOperativos(DateTime ActualizadoAt, DateOnly? Desde, DateOnly? Hasta,
    int ZonasActivas, int RutasEnCurso, int ParadasPendientes, int ZonasAltaCarga,
    IReadOnlyDictionary<string, int> IncidenciasPorEstado, int IncidenciasAbiertas,
    int IncidenciasCriticas, int IncidenciasSinResponsable, int IncidenciasFueraPlazo,
    int IncidenciasResueltasHoy, IReadOnlyDictionary<string, ResumenLiquidacionesEstado> LiquidacionesPorEstado,
    int TotalLiquidaciones, decimal NetoPeriodo,
    IReadOnlyDictionary<string, ResumenLiquidacionesEstado> LiquidacionesPilotosPorEstado,
    IReadOnlyDictionary<string, ResumenLiquidacionesEstado> LiquidacionesComerciosPorEstado,
    ResumenCobrosPeriodo? CobrosPeriodo = null);

public sealed partial class EnvirapidoOperationsService
{
    // No heredan los límites de las tablas. El piloto sólo recibe su propio ámbito.
    // Fecha de ruta / creación de incidencia / solapamiento de período financiero.
    // Zonas/carga y "resueltas hoy" son actuales, independientemente del filtro histórico.
    public async Task<IndicadoresOperativos> IndicadoresAsync(OperationsActor actor, DateOnly? desde = null, DateOnly? hasta = null)
    {
        await EnsureActorAsync(actor);
        var (from, until) = ReadPeriod(desde, hasta);
        var tenant = actor.TenantId; var now = Now;
        var routes = db.RutasOperativas.AsNoTracking().Where(r => r.ClienteAppId == tenant
            && (!actor.EsPiloto || r.PilotoId == actor.PilotoId)
            && (!desde.HasValue || r.Fecha >= desde.Value) && (!hasta.HasValue || r.Fecha <= hasta.Value));
        var routeCount = await routes.CountAsync(r => r.Estado == EstadoRutaOperativa.Activa);
        var pendingStops = await db.ParadasEntrega.AsNoTracking().CountAsync(p => p.ClienteAppId == tenant
            && (!actor.EsPiloto || p.OrdenEntrega.PilotoId == actor.PilotoId)
            && p.Estado == EstadoParadaEntrega.Pendiente && !EstadosEntrega.Terminales.Contains(p.OrdenEntrega.Estado)
            && db.RutaOrdenesOperativas.Any(m => m.ClienteAppId == tenant && m.OrdenEntregaId == p.OrdenEntregaId && m.Vigente
                && (m.Ruta.Estado == EstadoRutaOperativa.Planificada || m.Ruta.Estado == EstadoRutaOperativa.Activa)
                && (!desde.HasValue || m.Ruta.Fecha >= desde.Value) && (!hasta.HasValue || m.Ruta.Fecha <= hasta.Value)));
        var allowedIncidents = db.IncidenciasOperativas.AsNoTracking().Where(i => i.ClienteAppId == tenant
            && (!actor.EsPiloto || i.ReportadaPor == actor.UserId || i.OrdenEntrega!.PilotoId == actor.PilotoId || i.Ruta!.PilotoId == actor.PilotoId));
        var incidents = allowedIncidents.Where(i =>
            (!from.HasValue || i.CreatedAt >= from.Value) && (!until.HasValue || i.CreatedAt < until.Value));
        var states = await incidents.GroupBy(i => i.Estado).Select(g => new { Estado = g.Key, Cantidad = g.Count() }).ToListAsync();
        var incidentStates = Enum.GetValues<EstadoIncidencia>().ToDictionary(s => s.ToString(), s => states.Where(x => x.Estado == s).Select(x => x.Cantidad).SingleOrDefault());
        var open = incidents.Where(i => i.Estado != EstadoIncidencia.Resuelta);
        var critical = await open.CountAsync(i => i.Prioridad >= 4);
        var withoutOwner = await open.CountAsync(i => i.ResponsableId == null || i.ResponsableId == "");
        var overdue = await open.CountAsync(i => i.VenceAt < now);
        var (todayFrom, todayUntil) = ReadPeriod(Today, Today);
        var resolvedToday = await allowedIncidents.CountAsync(i => i.Estado == EstadoIncidencia.Resuelta
            && i.ResueltaAt >= todayFrom && i.ResueltaAt < todayUntil);
        // SQLite no agrega decimal con SUM. Proyectar únicamente estado e importe
        // conserva precisión decimal en ambos proveedores, sin cargar movimientos/PII.
        var moneyRows = await db.LiquidacionesOperativas.AsNoTracking().Where(l => l.ClienteAppId == tenant
            && (!actor.EsPiloto || l.PilotoId == actor.PilotoId)
            && (!desde.HasValue || l.Hasta >= desde.Value) && (!hasta.HasValue || l.Desde <= hasta.Value))
            .Select(l => new { l.Estado, l.Neto, l.PilotoId, l.TiendaId }).ToListAsync();
        var settlementStates = ResumenPorEstado(moneyRows.Select(l => (l.Estado, l.Neto)));
        var pilotStates = ResumenPorEstado(moneyRows.Where(l => l.PilotoId.HasValue).Select(l => (l.Estado, l.Neto)));
        var storeStates = ResumenPorEstado(moneyRows.Where(l => l.TiendaId.HasValue).Select(l => (l.Estado, l.Neto)));
        // La conciliación resume los cobros, no las liquidaciones. Mismo período
        // de creación que su tabla, pero sin heredar su límite de filas mostradas.
        var cashRows = await db.CobrosOperativos.AsNoTracking().Where(c => c.ClienteAppId == tenant
            && (!actor.EsPiloto || c.PilotoId == actor.PilotoId)
            && (!from.HasValue || c.CreatedAt >= from.Value) && (!until.HasValue || c.CreatedAt < until.Value))
            .Select(c => new { c.Importe, c.ImporteVerificado, c.ConciliadoAt,
                c.EntregadoEmpresaAt, c.ImporteEntregadoEmpresa }).ToListAsync();
        var verifiedCash = cashRows.Where(c => c.ConciliadoAt.HasValue && c.ImporteVerificado.HasValue).ToList();
        var receivedCash = cashRows.Where(c => c.EntregadoEmpresaAt.HasValue).ToList();
        var pendingCash = verifiedCash.Where(c => !c.EntregadoEmpresaAt.HasValue).ToList();
        var differences = verifiedCash.Where(c => c.Importe != c.ImporteVerificado).ToList();
        var cashSummary = new ResumenCobrosPeriodo(cashRows.Count, cashRows.Sum(c => c.Importe),
            verifiedCash.Count, verifiedCash.Sum(c => c.ImporteVerificado!.Value),
            receivedCash.Count, receivedCash.Sum(c => c.ImporteEntregadoEmpresa ?? 0m),
            pendingCash.Count, pendingCash.Sum(c => c.ImporteVerificado!.Value), differences.Count,
            differences.Sum(c => Math.Abs(c.Importe - c.ImporteVerificado!.Value)));
        var zoneCount = await db.ZonasOperativas.AsNoTracking().CountAsync(z => z.ClienteAppId == tenant && z.Activa
            && (!actor.EsPiloto || db.RutasOperativas.Any(r => r.ClienteAppId == tenant && r.PilotoId == actor.PilotoId && r.ZonaId == z.Id)));
        var highLoad = await ZonasAltaCargaActualesAsync(actor);
        return new(now, desde, hasta, zoneCount, routeCount, pendingStops, highLoad, incidentStates,
            incidentStates.Where(x => x.Key != nameof(EstadoIncidencia.Resuelta)).Sum(x => x.Value), critical, withoutOwner,
            overdue, resolvedToday, settlementStates, moneyRows.Count, moneyRows.Sum(l => l.Neto), pilotStates, storeStates,
            cashSummary);
    }

    private static IReadOnlyDictionary<string, ResumenLiquidacionesEstado> ResumenPorEstado(IEnumerable<(EstadoLiquidacion Estado, decimal Neto)> rows)
    {
        var grouped = rows.GroupBy(l => l.Estado).ToDictionary(g => g.Key, g => new ResumenLiquidacionesEstado(g.Count(), g.Sum(l => l.Neto)));
        return Enum.GetValues<EstadoLiquidacion>().ToDictionary(s => s.ToString(), s => grouped.GetValueOrDefault(s, new(0, 0m)));
    }

    private async Task<int> ZonasAltaCargaActualesAsync(OperationsActor actor)
    {
        var tenant = actor.TenantId;
        var zones = await db.ZonasOperativas.AsNoTracking().Where(z => z.ClienteAppId == tenant
            && (!actor.EsPiloto || db.RutasOperativas.Any(r => r.ClienteAppId == tenant && r.PilotoId == actor.PilotoId && r.ZonaId == z.Id))).ToListAsync();
        if (zones.Count == 0) return 0;
        var rows = await db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == tenant && !EstadosEntrega.Terminales.Contains(o.Estado))
            .Where(o => !actor.EsPiloto || o.PilotoId == actor.PilotoId)
            .Select(o => new { o.Id, o.Estado, o.PilotoId }).ToListAsync();
        var points = await db.ParadasEntrega.AsNoTracking().Where(p => p.ClienteAppId == tenant && !EstadosEntrega.Terminales.Contains(p.OrdenEntrega.Estado))
            .Where(p => !actor.EsPiloto || p.OrdenEntrega.PilotoId == actor.PilotoId)
            .Select(p => new { p.Id, p.OrdenEntregaId, p.Estado, p.Latitud, p.Longitud }).ToListAsync();
        var data = await db.DatosEnviosOperativos.AsNoTracking().Where(d => d.ClienteAppId == tenant
            && !EstadosEntrega.Terminales.Contains(d.OrdenEntrega.Estado)
            && (!actor.EsPiloto || d.OrdenEntrega.PilotoId == actor.PilotoId)).Select(d => new DatosEnvioOperativos {
                OrdenEntregaId = d.OrdenEntregaId, ZonaId = d.ZonaId, ProgramadaPara = d.ProgramadaPara, CanceladaAt = d.CanceladaAt }).ToListAsync();
        var stops = points.ToLookup(p => p.OrdenEntregaId);
        var orders = rows.Select(o => new OrdenEntrega { Id = o.Id, Estado = o.Estado, PilotoId = o.PilotoId,
            Paradas = stops[o.Id].Select(p => new ParadaEntrega { Id = p.Id, Estado = p.Estado, Latitud = p.Latitud, Longitud = p.Longitud }).ToList() }).ToList();
        // Punto-en-polígono sigue exactamente la política actual de ZoneDto;
        // sólo coordenadas y estados, nunca una lista truncada ni direcciones.
        return zones.Count(z => ZoneDto(z, orders, [], data).AltaCarga);
    }
}
