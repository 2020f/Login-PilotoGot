using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

// Todos los métodos mutables se llaman después de adquirir el bloqueo del tenant.
public sealed class OperationsWorkGuard(ApplicationDbContext db, TimeProvider? clock = null)
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task ValidateAssignmentAsync(int tenant, int orden, int piloto)
    {
        await CheckScheduleAsync(tenant, orden);
        var membership = await db.RutaOrdenesOperativas.AsNoTracking().Include(x => x.Ruta)
            .SingleOrDefaultAsync(x => x.ClienteAppId == tenant && x.OrdenEntregaId == orden && x.ParadaEntregaId == null && x.Vigente);
        await CheckPauseAsync(tenant, orden, null, membership?.RutaId);
        if (membership is not null && !(membership.Ruta.Estado == EstadoRutaOperativa.Activa && membership.Ruta.PilotoId == piloto))
            throw new OperationsException(409, "La orden está reservada por una ruta; utiliza su asignación autorizada.");
    }

    public async Task ValidateParadaAssignmentAsync(int tenant, int orden, int parada, int piloto)
    {
        await CheckScheduleAsync(tenant, orden);
        var route = await RouteAsync(tenant, orden, parada);
        await CheckPauseAsync(tenant, orden, parada, route?.Id);
        if (route is not null && !(route.Estado == EstadoRutaOperativa.Activa && route.PilotoId == piloto))
            throw new OperationsException(409, "La parada está reservada por otra ruta; retírala de ese recorrido antes de asignar.");
    }

    public async Task ValidateAsync(int tenant, int orden, int? parada, bool esRecogida)
    {
        var schedule = await db.DatosEnviosOperativos.AsNoTracking()
            .SingleOrDefaultAsync(d => d.ClienteAppId == tenant && d.OrdenEntregaId == orden);
        var route = await RouteAsync(tenant, orden, parada);
        var incidents = await OpenPauses(tenant).ToListAsync();
        ValidateLoaded(schedule, route, incidents, orden, parada, esRecogida, Now);
    }

    // Una carga por petición, independiente de cuántas órdenes/paradas se proyecten.
    // Es orientación de UI; los comandos vuelven a consultar bajo bloqueo del tenant.
    public async Task<ReadPolicy> LoadReadPolicyAsync(int tenant, IReadOnlyCollection<int> orderIds)
    {
        if (orderIds.Count == 0) return new ReadPolicy(Now, [], [], []);
        var ids = orderIds.ToArray();
        var schedules = await db.DatosEnviosOperativos.AsNoTracking()
            .Where(d => d.ClienteAppId == tenant && ids.Contains(d.OrdenEntregaId)).ToListAsync();
        var routes = await RoutesQuery(tenant).Where(r => r.Ordenes.Any(x => x.Vigente && ids.Contains(x.OrdenEntregaId))).ToListAsync();
        var incidents = await OpenPauses(tenant).ToListAsync();
        return new ReadPolicy(Now, schedules, routes, incidents);
    }

    public sealed class ReadPolicy(DateTime now, IReadOnlyList<DatosEnvioOperativos> schedules,
        IReadOnlyList<RutaOperativa> routes, IReadOnlyList<IncidenciaOperativa> incidents)
    {
        private readonly Dictionary<int, DatosEnvioOperativos> _schedules = schedules.ToDictionary(x => x.OrdenEntregaId);
        private readonly Dictionary<(int OrderId, int? StopId), RutaOperativa> _routes = routes.SelectMany(r => r.Ordenes.Where(x => x.Vigente)
            .Select(x => new { Key = (x.OrdenEntregaId, x.ParadaEntregaId), Route = r })).ToDictionary(x => x.Key, x => x.Route);
        public string? MotivoBloqueo(int orderId, int? stopId, bool pickup)
        {
            try { ValidateLoaded(_schedules.GetValueOrDefault(orderId), _routes.GetValueOrDefault((orderId, stopId))
                    ?? _routes.GetValueOrDefault((orderId, null)),
                incidents, orderId, stopId, pickup, now); return null; }
            catch (OperationsException ex) { return ex.Message; }
        }
    }

    private static void ValidateLoaded(DatosEnvioOperativos? schedule, RutaOperativa? route,
        IReadOnlyList<IncidenciaOperativa> incidents, int orden, int? parada, bool esRecogida, DateTime now)
    {
        if (schedule is not null && (schedule.CanceladaAt.HasValue || schedule.ProgramadaPara > now))
            throw new OperationsException(409, "La orden está cancelada o aún no está habilitada por su programación.");
        EnsureNotPaused(incidents, orden, parada, route?.Id,
            route?.Ordenes.Where(x => x.Vigente).Select(x => x.OrdenEntregaId).ToHashSet() ?? [],
            route?.Puntos.Where(p => p.RutaOrden.Vigente && p.ParadaEntregaId.HasValue)
                .Select(p => p.ParadaEntregaId!.Value).ToHashSet() ?? []);
        if (route is null) return;
        if (route.Estado != EstadoRutaOperativa.Activa)
            throw new OperationsException(409, "La ruta todavía no está activa.");
        var next = route.Puntos.OrderBy(p => p.Posicion).FirstOrDefault(p => !Completed(p, route.PilotoId));
        if (next is null || next.OrdenEntregaId != orden || (esRecogida
                ? next.Tipo != TipoPuntoRuta.Recogida || parada.HasValue && next.ParadaEntregaId != parada
                : next.Tipo != TipoPuntoRuta.Entrega || next.ParadaEntregaId != parada))
            throw new OperationsException(409, "La próxima visita cambió; actualiza la secuencia autorizada.");
    }

    public async Task ValidateRetornoAsync(int tenant, int orden, int parada)
    {
        var route = await RouteAsync(tenant, orden, parada);
        await CheckPauseAsync(tenant, orden, parada, route?.Id);
        if (route is not null && route.Estado != EstadoRutaOperativa.Activa)
            throw new OperationsException(409, "La ruta no está activa para retornar.");
        if (route is not null)
        {
            var next = route.Puntos.OrderBy(p => p.Posicion).FirstOrDefault(p => !Completed(p, route.PilotoId));
            if (next is null || next.Tipo != TipoPuntoRuta.Devolucion || next.OrdenEntregaId != orden || next.ParadaEntregaId != parada)
                throw new OperationsException(409, "El retorno debe respetar la próxima visita autorizada de la ruta.");
        }
    }

    private async Task CheckScheduleAsync(int tenant, int orden)
    {
        if (await db.DatosEnviosOperativos.AnyAsync(d => d.ClienteAppId == tenant && d.OrdenEntregaId == orden
            && (d.CanceladaAt != null || d.ProgramadaPara > Now)))
            throw new OperationsException(409, "La orden está cancelada o aún no está habilitada por su programación.");
    }

    private IQueryable<RutaOperativa> RoutesQuery(int tenant) => db.RutasOperativas.AsNoTracking()
        .Include(r => r.Ordenes).ThenInclude(x => x.OrdenEntrega)
        .Include(r => r.Puntos).ThenInclude(p => p.ParadaEntrega).AsSplitQuery()
        .Include(r => r.Puntos).ThenInclude(p => p.RutaOrden).ThenInclude(x => x.OrdenEntrega)
        .Where(r => r.ClienteAppId == tenant);

    private Task<RutaOperativa?> RouteAsync(int tenant, int orden, int? parada = null) => RoutesQuery(tenant)
        .Where(r => r.Ordenes.Any(x => x.OrdenEntregaId == orden && x.Vigente
            && (x.ParadaEntregaId == null || parada.HasValue && x.ParadaEntregaId == parada)))
        .OrderByDescending(r => parada.HasValue && r.Ordenes.Any(x => x.OrdenEntregaId == orden && x.Vigente && x.ParadaEntregaId == parada))
        .FirstOrDefaultAsync();

    private IQueryable<IncidenciaOperativa> OpenPauses(int tenant) => db.IncidenciasOperativas.AsNoTracking()
        .Where(i => i.ClienteAppId == tenant && i.Estado != EstadoIncidencia.Resuelta && i.Efecto != EfectoIncidencia.Continuar);

    private async Task CheckPauseAsync(int tenant, int orden, int? parada, int? ruta)
    {
        var incidents = await OpenPauses(tenant).ToListAsync();
        var routeOrders = ruta.HasValue ? (await db.RutaOrdenesOperativas.AsNoTracking()
            .Where(x => x.ClienteAppId == tenant && x.RutaId == ruta && x.Vigente).Select(x => x.OrdenEntregaId).ToListAsync()).ToHashSet() : [];
        var routeStops = ruta.HasValue ? (await db.PuntosRutaOperativos.AsNoTracking()
            .Where(x => x.ClienteAppId == tenant && x.RutaId == ruta && x.RutaOrden.Vigente && x.ParadaEntregaId.HasValue)
            .Select(x => x.ParadaEntregaId!.Value).ToListAsync()).ToHashSet() : [];
        EnsureNotPaused(incidents, orden, parada, ruta, routeOrders, routeStops);
    }

    private static void EnsureNotPaused(IReadOnlyList<IncidenciaOperativa> incidents, int orden, int? parada,
        int? ruta, HashSet<int> routeOrders, HashSet<int> routeStops)
    {
        foreach (var i in incidents)
        {
            var sameRoute = ruta.HasValue && (i.RutaId.HasValue ? i.RutaId == ruta
                : i.OrdenEntregaId.HasValue && routeOrders.Contains(i.OrdenEntregaId.Value)
                    && (!i.ParadaEntregaId.HasValue || routeStops.Contains(i.ParadaEntregaId.Value)));
            var sameOrder = i.OrdenEntregaId == orden
                && (!i.ParadaEntregaId.HasValue || !parada.HasValue || i.ParadaEntregaId == parada);
            var sameRouteOrder = sameOrder && (!i.RutaId.HasValue || i.RutaId == ruta);
            if (i.Efecto == EfectoIncidencia.PausarRuta && (sameRoute || sameRouteOrder)
                || i.Efecto == EfectoIncidencia.PausarParada && (i.ParadaEntregaId.HasValue
                    ? i.ParadaEntregaId == parada && sameOrder
                    : sameOrder || sameRoute))
                throw new OperationsException(409, "La política de una incidencia pausa este trabajo. Requiere atención autorizada.");
        }
    }

    public async Task ReconcileCompletedRoutesAsync(int tenant, int orden)
    {
        var routes = await db.RutasOperativas.Include(r => r.Ordenes).ThenInclude(x => x.OrdenEntrega)
            .Include(r => r.Ordenes).ThenInclude(x => x.ParadaEntrega)
            .Where(r => r.ClienteAppId == tenant && (r.Estado == EstadoRutaOperativa.Activa || r.Estado == EstadoRutaOperativa.Planificada)
                && r.Ordenes.Any(x => x.OrdenEntregaId == orden && x.Vigente)).AsSplitQuery().ToListAsync();
        foreach (var route in routes.Where(r => r.Ordenes.All(x => !x.Vigente || (x.ParadaEntregaId.HasValue
            ? x.ParadaEntrega is not null && (EstadosEntrega.EsTerminal(x.ParadaEntrega.Estado) || x.ParadaEntrega.EnReasignacion)
            : EstadosEntrega.EsTerminal(x.OrdenEntrega.Estado)))))
        {
            route.Estado = route.Ordenes.All(x => x.ParadaEntregaId.HasValue
                ? x.ParadaEntrega?.Estado == EstadoParadaEntrega.Cancelada
                : x.OrdenEntrega.Estado == Login.Domain.Enums.EstadoOrden.Cancelada)
                ? EstadoRutaOperativa.Cancelada : EstadoRutaOperativa.Finalizada;
            route.FinalizadaAt = Now;
            route.UpdatedAt = Now; route.Version = Guid.NewGuid().ToString("N");
            foreach (var member in route.Ordenes) member.Vigente = false;
            db.EventosOperativos.Add(new() { ClienteAppId = tenant, ActorId = "System", Tipo = "RutaFinalizada",
                RecursoId = route.Id, CreatedAt = Now, Descripcion = "Todas las órdenes terminaron, conservando entregas y devoluciones por separado." });
        }
        await db.SaveChangesAsync();
    }

    private static bool Completed(PuntoRutaOperativa point, int? routePilotId) => !point.RutaOrden.Vigente
        || point.RutaOrden.OrdenEntrega.Estado == Login.Domain.Enums.EstadoOrden.Cancelada
        || point.RutaOrden.OrdenEntrega.GestionPorParadas && point.ParadaEntrega is not null
            && (point.ParadaEntrega.EnReasignacion || point.ParadaEntrega.Estado == EstadoParadaEntrega.Cancelada
                || point.ParadaEntrega.PilotoAsignadoId != routePilotId)
        || (point.Tipo == TipoPuntoRuta.Recogida
        ? point.RecogidaConfirmadaAt.HasValue || (point.RutaOrden.OrdenEntrega.GestionPorParadas
            ? point.ParadaEntrega?.RecogidaParadaAt.HasValue == true : point.RutaOrden.OrdenEntrega.RecolectadaAt.HasValue)
        : point.Tipo == TipoPuntoRuta.Entrega
            ? point.ParadaEntrega is not null && point.ParadaEntrega.Estado is EstadoParadaEntrega.Entregada
                or EstadoParadaEntrega.DevolucionAutorizada or EstadoParadaEntrega.EnRetorno or EstadoParadaEntrega.Devuelta
            : point.ParadaEntrega?.Estado == EstadoParadaEntrega.Devuelta);
}
