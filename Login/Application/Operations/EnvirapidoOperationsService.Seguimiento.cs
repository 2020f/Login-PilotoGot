using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    public async Task<RecorridoEstimado?> SeguimientoPedidoAsync(OperationsActor actor, int ordenId, int? paradaId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureActorAsync(actor);
        var data = await CargarSeguimientosAsync(actor, [ordenId], cancellationToken);
        if (!data.Orders.TryGetValue(ordenId, out var order)) throw new OperationsException(404, "Orden no disponible.");
        if (paradaId.HasValue && !order.Paradas.Any(p => p.Id == paradaId)) throw new OperationsException(404, "Parada no disponible.");
        return await EstimarSeguimientoAsync(PrepararSeguimiento(actor, order, paradaId, data), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, RecorridoEstimado?>> SeguimientoPedidosAsync(OperationsActor actor,
        IReadOnlyCollection<int> ordenIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureActorAsync(actor);
        if (ordenIds is null || ordenIds.Count is < 1 or > 50 || ordenIds.Any(id => id <= 0) || ordenIds.Distinct().Count() != ordenIds.Count)
            throw new OperationsException(400, "Consulta de uno a cincuenta pedidos distintos.");
        var data = await CargarSeguimientosAsync(actor, ordenIds, cancellationToken);
        // Ninguna consulta concurrente al DbContext: el modelo de lectura ya está
        // materializado. Sólo hasta cuatro cálculos HTTP externos simultáneos.
        using var limit = new SemaphoreSlim(4);
        var tasks = data.Orders.Values.Select(async order =>
        {
            await limit.WaitAsync(cancellationToken);
            try { return (order.Id, Estimate: await EstimarSeguimientoAsync(PrepararSeguimiento(actor, order, null, data), cancellationToken)); }
            finally { limit.Release(); }
        }).ToArray();
        var result = await Task.WhenAll(tasks);
        return result.ToDictionary(x => x.Id, x => x.Estimate);
    }

    private sealed record SeguimientoLectura(Dictionary<int, OrdenEntrega> Orders,
        Dictionary<(int OrdenId, int? ParadaId), RutaOperativa> Routes, Dictionary<int, Piloto> Pilots);
    private sealed record PlanSeguimiento(int Resource, string Revision, Piloto Pilot, List<PuntoRutaDto> Points, int TargetPoint);

    private async Task<SeguimientoLectura> CargarSeguimientosAsync(OperationsActor actor, IReadOnlyCollection<int> ids, CancellationToken token)
    {
        var orders = await db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == actor.TenantId && ids.Contains(o.Id)
                && (!actor.EsPiloto || (!o.GestionPorParadas && o.PilotoId == actor.PilotoId)
                    || (o.GestionPorParadas && o.Paradas.Any(p => p.PilotoAsignadoId == actor.PilotoId && !p.EnReasignacion))))
            .Include(o => o.Paradas).AsSplitQuery().ToDictionaryAsync(o => o.Id, token);
        var orderIds = orders.Keys.ToArray();
        var pilotsIds = orders.Values.Where(o => !EstadosEntrega.Terminales.Contains(o.Estado))
            .SelectMany(o => o.GestionPorParadas ? o.Paradas.Where(p => p.PilotoAsignadoId.HasValue).Select(p => p.PilotoAsignadoId!.Value)
                : o.PilotoId.HasValue ? new[] { o.PilotoId.Value } : []).Distinct().ToArray();
        var pilots = await db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == actor.TenantId && pilotsIds.Contains(p.Id) && p.Activo)
            .ToDictionaryAsync(p => p.Id, token);
        var routes = await db.RutasOperativas.AsNoTrackingWithIdentityResolution().Where(r => r.ClienteAppId == actor.TenantId
                && r.Estado == EstadoRutaOperativa.Activa && (!actor.EsPiloto || r.PilotoId == actor.PilotoId)
                && r.Ordenes.Any(x => orderIds.Contains(x.OrdenEntregaId) && x.Vigente))
            .Include(r => r.Puntos).ThenInclude(p => p.ParadaEntrega)
            .Include(r => r.Puntos).ThenInclude(p => p.RutaOrden).ThenInclude(m => m.OrdenEntrega)
            .Include(r => r.Ordenes).ThenInclude(x => x.OrdenEntrega).ThenInclude(o => o.Paradas).AsSplitQuery().ToListAsync(token);
        return new(orders, routes.SelectMany(r => r.Ordenes.Where(m => m.Vigente).Select(m => new { Key = (m.OrdenEntregaId, m.ParadaEntregaId), Route = r }))
            .ToDictionary(x => x.Key, x => x.Route), pilots);
    }

    private static PlanSeguimiento? PrepararSeguimiento(OperationsActor actor, OrdenEntrega order, int? stopId, SeguimientoLectura data)
    {
        if (EstadosEntrega.Terminales.Contains(order.Estado)) return null;
        var target = order.Paradas.Where(p => p.Estado == EstadoParadaEntrega.Pendiente && (!stopId.HasValue || p.Id == stopId)
                && (!order.GestionPorParadas || !p.EnReasignacion && p.PilotoAsignadoId.HasValue && (!actor.EsPiloto || p.PilotoAsignadoId == actor.PilotoId)))
            .OrderBy(p => p.Posicion).FirstOrDefault();
        if (target is null) return null;
        var pilotId = order.GestionPorParadas ? target.PilotoAsignadoId : order.PilotoId;
        if (!pilotId.HasValue || !data.Pilots.TryGetValue(pilotId.Value, out var pilot)) return null;
        if (actor.EsPiloto && (order.GestionPorParadas ? !target.RecogidaParadaAt.HasValue || target.PilotoCustodioId != actor.PilotoId : !order.RecolectadaAt.HasValue)) return null;
        var points = new List<PuntoRutaDto>();
        if (data.Routes.TryGetValue((order.Id, order.GestionPorParadas ? target.Id : null), out var route))
        {
            if (route.PilotoId != pilotId) return null;
            foreach (var point in route.Puntos.OrderBy(p => p.Posicion))
            {
                if (PointCompleted(point) || PointExcluded(point)) continue;
                var source = point.RutaOrden.OrdenEntrega;
                var managed = source.GestionPorParadas && point.ParadaEntregaId.HasValue;
                var stop = point.ParadaEntrega;
                // Validar TODA la ruta antes de reutilizar una geometría de CRM;
                // ni un prefijo permite saltarse la privacidad de órdenes sin B.
                if (actor.EsPiloto && (managed
                    ? stop is null || stop.EnReasignacion || stop.PilotoAsignadoId != actor.PilotoId
                        || point.Tipo == TipoPuntoRuta.Entrega && (!stop.RecogidaParadaAt.HasValue || stop.PilotoCustodioId != actor.PilotoId)
                    : source.PilotoId != actor.PilotoId || point.Tipo == TipoPuntoRuta.Entrega && !source.RecolectadaAt.HasValue)) return null;
                var pickup = point.Tipo is TipoPuntoRuta.Recogida or TipoPuntoRuta.Devolucion;
                points.Add(new(point.Id, source.Id, point.ParadaEntregaId, point.Tipo.ToString(), point.Posicion,
                    pickup ? managed ? stop?.RecogidaActualNombre ?? source.RecogidaNombre : source.RecogidaNombre : stop!.Nombre,
                    pickup ? managed ? stop?.RecogidaActualDireccion ?? source.RecogidaDireccion : source.RecogidaDireccion : stop!.Direccion,
                    pickup ? managed ? stop?.RecogidaActualLatitud ?? source.RecogidaLatitud : source.RecogidaLatitud : stop!.Latitud,
                    pickup ? managed ? stop?.RecogidaActualLongitud ?? source.RecogidaLongitud : source.RecogidaLongitud : stop!.Longitud, false, true, point.LlegadaAt));
            }
            var pendingIds = order.Paradas.Where(p => p.Estado == EstadoParadaEntrega.Pendiente).Select(p => p.Id).ToHashSet();
            var pointId = points.FirstOrDefault(p => p.OrdenId == order.Id && p.Tipo == nameof(TipoPuntoRuta.Entrega)
                && p.ParadaId.HasValue && pendingIds.Contains(p.ParadaId.Value) && (!stopId.HasValue || p.ParadaId == stopId))?.Id;
            return pointId.HasValue ? new(route.Id, route.Version, pilot, points, pointId.Value) : null;
        }
        if (order.GestionPorParadas)
        {
            var assigned = order.Paradas.Where(p => p.Estado == EstadoParadaEntrega.Pendiente && !p.EnReasignacion && p.PilotoAsignadoId == pilotId).OrderBy(p => p.Posicion).ToList();
            if (actor.EsPiloto && assigned.Any(p => !p.RecogidaParadaAt.HasValue || p.PilotoCustodioId != actor.PilotoId)) return null;
            foreach (var stop in assigned.Where(p => !p.RecogidaParadaAt.HasValue))
                points.Add(new(-stop.Id, order.Id, stop.Id, "Recogida", points.Count + 1,
                    stop.RecogidaActualNombre ?? order.RecogidaNombre, stop.RecogidaActualDireccion ?? order.RecogidaDireccion,
                    stop.RecogidaActualLatitud ?? order.RecogidaLatitud, stop.RecogidaActualLongitud ?? order.RecogidaLongitud, false, true, null));
            foreach (var stop in assigned)
                points.Add(new(stop.Id, order.Id, stop.Id, "Entrega", points.Count + 1, stop.Nombre, stop.Direccion, stop.Latitud, stop.Longitud, false, true, null));
            var managedRevision = $"piloto:{pilotId}:paradas:{string.Join(',', assigned.Select(p => $"{p.Id}-{p.RevisionAsignacion}-{p.RecogidaParadaAt?.Ticks}-{(int)p.Estado}"))}";
            return new(-order.Id, managedRevision, pilot, points, target.Id);
        }
        if (!order.RecolectadaAt.HasValue)
            points.Add(new(-order.Id, order.Id, null, "Recogida", 0, order.RecogidaNombre, order.RecogidaDireccion,
                order.RecogidaLatitud, order.RecogidaLongitud, false, true, null));
        // Sin ruta también se calcula una sola vez la secuencia completa B + C
        // pendientes, y se deriva cada destinatario del mismo resultado.
        foreach (var stop in order.Paradas.OrderBy(p => p.Posicion).Where(p => p.Estado == EstadoParadaEntrega.Pendiente))
            points.Add(new(stop.Id, order.Id, stop.Id, "Entrega", stop.Posicion, stop.Nombre, stop.Direccion, stop.Latitud, stop.Longitud, false, true, null));
        var revision = $"piloto:{order.PilotoId}:estado:{(int)order.Estado}:asignada:{order.AssignedAt?.Ticks}:B:{order.RecolectadaAt?.Ticks}:paradas:{string.Join(',', order.Paradas.OrderBy(p => p.Posicion).Select(p => $"{p.Id}-{(int)p.Estado}"))}";
        return new(-order.Id, revision, pilot, points, target.Id);
    }

    private async Task<RecorridoEstimado?> EstimarSeguimientoAsync(PlanSeguimiento? plan, CancellationToken token)
    {
        if (plan is null) return null;
        var full = await recorridos.EstimarAsync(plan.Resource, plan.Revision, plan.Pilot.Latitud,
            plan.Pilot.Longitud, plan.Pilot.UltimaUbicacionAt, plan.Points, token);
        return full?.HastaPunto(plan.TargetPoint);
    }
}
