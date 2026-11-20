using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    public async Task<object> GuardarZonaAsync(OperationsActor a, GuardarZonaRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "Zona", r.OperacionId, r, async () =>
        {
            var polygon = GeoGeometry.Validate(r.Poligono);
            if (r.Capacidad is < 1 or > 100000 || r.AvisoPorcentaje is < 1 or > 100 || r.UnidadCapacidad is not ("ParadasPendientes" or "OrdenesAbiertas")) throw new OperationsException(400, "Configura la unidad, capacidad y porcentaje de aviso.");
            if (!Regex.IsMatch(r.Color ?? "", "^#[0-9a-fA-F]{6}$") || !TimeOnly.TryParseExact(r.HoraInicio, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || !TimeOnly.TryParseExact(r.HoraFin, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new OperationsException(400, "El color o el horario de la zona no es válido.");
            var name = Required(r.Nombre, 100, "Nombre");
            if (await db.ZonasOperativas.AnyAsync(z => z.ClienteAppId == a.TenantId && z.Nombre == name && z.Id != r.Id)) throw new OperationsException(400, "Ya existe una zona con ese nombre.");
            var zone = r.Id.HasValue ? await db.ZonasOperativas.SingleOrDefaultAsync(z => z.ClienteAppId == a.TenantId && z.Id == r.Id) ?? throw new OperationsException(404, "Zona no disponible.") : new ZonaOperativa { ClienteAppId = a.TenantId, CreatedAt = Now };
            if (r.Id.HasValue) CheckVersion(zone, r.Version); else db.ZonasOperativas.Add(zone);
            zone.Nombre = name; zone.Region = Required(r.Region, 100, "Región"); zone.Color = r.Color;
            zone.PoligonoJson = JsonSerializer.Serialize(polygon, Json); zone.SuperficieKm2 = GeoGeometry.Area(polygon);
            zone.Capacidad = r.Capacidad; zone.UnidadCapacidad = r.UnidadCapacidad; zone.AvisoPorcentaje = r.AvisoPorcentaje;
            zone.HoraInicio = r.HoraInicio; zone.HoraFin = r.HoraFin; zone.Activa = r.Activa; Touch(zone);
            await db.SaveChangesAsync(); Audit(a, "Zona", zone.Id, "Límites y reglas de cobertura guardados.");
            return new { id = zone.Id, version = zone.Version };
        });
    }

    public async Task<object> CrearRutaAsync(OperationsActor a, CrearRutaRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "CrearRuta", r.OperacionId, r, async () =>
        {
            if (r.OrdenIds is null || r.OrdenIds.Count is < 1 or > 100 || r.OrdenIds.Any(x => x <= 0) || r.OrdenIds.Distinct().Count() != r.OrdenIds.Count) throw new OperationsException(400, "Selecciona de una a cien órdenes distintas.");
            var selectedIds = r.ParadaIds ?? [];
            if (selectedIds.Count > 500 || selectedIds.Any(x => x <= 0) || selectedIds.Distinct().Count() != selectedIds.Count) throw new OperationsException(400, "Selecciona paradas distintas y válidas.");
            if (r.Fecha < Today.AddDays(-1) || r.Fecha > Today.AddYears(1)) throw new OperationsException(400, "La fecha de planificación está fuera del intervalo permitido.");
            if (r.ZonaId.HasValue && !await db.ZonasOperativas.AnyAsync(z => z.Id == r.ZonaId && z.ClienteAppId == a.TenantId && z.Activa)) throw new OperationsException(400, "Zona no disponible.");
            if (r.PilotoId.HasValue) await AvailablePilotAsync(a, r.PilotoId.Value);
            var orders = await db.OrdenesEntrega.Include(o => o.Paradas).Where(o => r.OrdenIds.Contains(o.Id) && o.ClienteAppId == a.TenantId).ToListAsync();
            if (orders.Count != r.OrdenIds.Count || orders.Any(o => EstadosEntrega.Terminales.Contains(o.Estado))) throw new OperationsException(400, "Todas las órdenes deben estar disponibles en esta empresa.");
            var legacy = orders.Where(o => !o.GestionPorParadas).ToList();
            var managed = orders.Where(o => o.GestionPorParadas).ToList();
            if (legacy.Any(o => o.Estado is not (EstadoOrden.Creada or EstadoOrden.Asignada) || (o.PilotoId.HasValue && o.PilotoId != r.PilotoId))) throw new OperationsException(400, "Las órdenes completas deben pertenecer al piloto de la ruta o estar sin asignar.");
            var stops = managed.SelectMany(o => o.Paradas).Where(p => selectedIds.Contains(p.Id)).ToList();
            if (stops.Count != selectedIds.Count || managed.Any(o => !stops.Any(p => p.OrdenEntregaId == o.Id))) throw new OperationsException(400, "Selecciona expresamente las paradas de cada orden gestionada; no se incluyen las demás entregas.");
            foreach (var stop in stops) ValidateManagedRouteStop(stop, r.PilotoId);
            var legacyIds = legacy.Select(o => o.Id).ToArray();
            var managedIds = managed.Select(o => o.Id).ToArray();
            if (await db.RutaOrdenesOperativas.AnyAsync(x => x.ClienteAppId == a.TenantId && x.Vigente &&
                (legacyIds.Contains(x.OrdenEntregaId) || (x.ParadaEntregaId.HasValue && selectedIds.Contains(x.ParadaEntregaId.Value)) || (!x.ParadaEntregaId.HasValue && managedIds.Contains(x.OrdenEntregaId))))) throw new OperationsException(409, "Una unidad seleccionada ya está incluida en otro recorrido.");
            if (await db.DatosEnviosOperativos.AnyAsync(d => d.ClienteAppId == a.TenantId && r.OrdenIds.Contains(d.OrdenEntregaId) && d.CanceladaAt != null)) throw new OperationsException(400, "La selección contiene una orden cancelada.");
            if (orders.Any(o => o.Paradas.Count == 0) || legacy.Sum(o => o.Paradas.Count + 1) + stops.Sum(p => p.RecogidaParadaAt.HasValue ? 1 : 2) > 500) throw new OperationsException(400, "La ruta requiere paradas migradas y admite hasta 500 puntos.");
            var route = new RutaOperativa { ClienteAppId = a.TenantId, Nombre = Required(r.Nombre, 120, "Nombre"), Fecha = r.Fecha,
                ZonaId = r.ZonaId, PilotoId = r.PilotoId, PermitirReordenarPiloto = r.PermitirReordenarPiloto, CreatedAt = Now, UpdatedAt = Now };
            foreach (var orderId in r.OrdenIds)
            {
                var order = orders.Single(o => o.Id == orderId);
                if (!order.GestionPorParadas) route.Ordenes.Add(new() { ClienteAppId = a.TenantId, OrdenEntregaId = orderId });
                else foreach (var stop in stops.Where(p => p.OrdenEntregaId == orderId).OrderBy(p => p.Posicion))
                    route.Ordenes.Add(new() { ClienteAppId = a.TenantId, OrdenEntregaId = orderId, ParadaEntregaId = stop.Id, ParadaEntrega = stop });
            }
            db.RutasOperativas.Add(route); await db.SaveChangesAsync();
            var position = 1;
            // The planner exposes this initial sequence; subsequent changes are explicit commands.
            foreach (var entry in route.Ordenes)
                if (!entry.ParadaEntregaId.HasValue || !entry.ParadaEntrega!.RecogidaParadaAt.HasValue)
                    route.Puntos.Add(new() { ClienteAppId = a.TenantId, RutaId = route.Id, OrdenEntregaId = entry.OrdenEntregaId,
                        RutaOrden = entry, ParadaEntregaId = entry.ParadaEntregaId, Tipo = TipoPuntoRuta.Recogida, Posicion = position++ });
            foreach (var entry in route.Ordenes)
                foreach (var stop in entry.ParadaEntregaId.HasValue ? new[] { entry.ParadaEntrega! } : orders.Single(o => o.Id == entry.OrdenEntregaId).Paradas.OrderBy(p => p.Posicion).ToArray())
                    route.Puntos.Add(new() { ClienteAppId = a.TenantId, RutaId = route.Id, OrdenEntregaId = entry.OrdenEntregaId,
                        RutaOrden = entry, ParadaEntregaId = stop.Id, Tipo = TipoPuntoRuta.Entrega, Posicion = position++ });
            await db.SaveChangesAsync(); Audit(a, "Ruta", route.Id, $"Ruta planificada con {orders.Count} órdenes; cada recogida precede a sus entregas.");
            return new { id = route.Id, version = route.Version };
        });
    }

    public async Task<object> AsignarRutaAsync(OperationsActor a, int id, AsignarRutaRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "AsignarRuta", r.OperacionId, new { id, r }, async () =>
        {
            var route = await LoadRouteAsync(a, id); CheckVersion(route, r.Version);
            if (route.Estado != EstadoRutaOperativa.Planificada) throw new OperationsException(400, "Solo puedes asignar una ruta planificada.");
            await AvailablePilotAsync(a, r.PilotoId);
            foreach (var entry in route.Ordenes.Where(x => x.Vigente))
            {
                if (entry.ParadaEntregaId.HasValue) ValidateManagedRouteStop(entry.ParadaEntrega!, r.PilotoId);
                else if (!EstadosEntrega.Terminales.Contains(entry.OrdenEntrega.Estado) && entry.OrdenEntrega.PilotoId.HasValue && entry.OrdenEntrega.PilotoId != r.PilotoId) throw new OperationsException(409, "Una orden ya tiene otro piloto. Revisa su asignación antes de continuar.");
            }
            route.PilotoId = r.PilotoId; Touch(route); Audit(a, "Ruta", id, $"Piloto {r.PilotoId} seleccionado para la ruta planificada.");
            return new { id, version = route.Version };
        });
    }

    public async Task<object> EstadoRutaAsync(OperationsActor a, int id, EstadoRutaRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "EstadoRuta", r.OperacionId, new { id, r }, async () =>
        {
            var route = await LoadRouteAsync(a, id); CheckVersion(route, r.Version);
            if (r.Accion == "Iniciar")
            {
                if (route.Estado != EstadoRutaOperativa.Planificada || route.PilotoId is null) throw new OperationsException(400, "Asigna un piloto a la ruta planificada antes de iniciarla.");
                await AvailablePilotAsync(a, route.PilotoId.Value);
                foreach (var entry in route.Ordenes)
                {
                    var order = entry.OrdenEntrega;
                    if (!entry.Vigente || order.Estado == EstadoOrden.Cancelada) continue;
                    if (entry.ParadaEntregaId.HasValue)
                    {
                        if (!order.GestionPorParadas) throw new OperationsException(409, "El tipo de gestión de la orden cambió. Actualiza el recorrido.");
                        var stop = entry.ParadaEntrega!;
                        ValidateManagedRouteStop(stop, route.PilotoId);
                        if (await db.DatosEnviosOperativos.AnyAsync(d => d.OrdenEntregaId == order.Id && d.ClienteAppId == a.TenantId && (d.CanceladaAt != null || d.ProgramadaPara > Now))) throw new OperationsException(400, "Una parada todavía no está habilitada para despacho.");
                        if (stop.PilotoAsignadoId != route.PilotoId) stop.RevisionAsignacion = Guid.NewGuid().ToString("N");
                        stop.PilotoAsignadoId = route.PilotoId;
                        order.AssignedAt ??= Now;
                        ReconcileManagedOrder(order, Now);
                        db.HistorialOrdenes.Add(new() { OrdenEntregaId = order.Id, TipoEvento = TipoEventoOrden.Asignada,
                            ActorIdentityUserId = a.UserId, ActorRol = "Gestor", CreatedAt = Now, Descripcion = $"Parada {stop.Id} asignada mediante ruta R-{route.Id:000}." });
                        continue;
                    }
                    if (order.GestionPorParadas) throw new OperationsException(409, "Replanifica esta orden seleccionando sus paradas disponibles.");
                    if (order.Estado is not (EstadoOrden.Creada or EstadoOrden.Asignada) || (order.PilotoId.HasValue && order.PilotoId != route.PilotoId)) throw new OperationsException(409, "Las órdenes de la ruta cambiaron. Actualiza el recorrido.");
                    if (await db.DatosEnviosOperativos.AnyAsync(d => d.OrdenEntregaId == order.Id && d.ClienteAppId == a.TenantId && (d.CanceladaAt != null || d.ProgramadaPara > Now))) throw new OperationsException(400, "Una orden está cancelada o todavía no está habilitada por su programación.");
                    order.PilotoId = route.PilotoId; order.Estado = EstadoOrden.Asignada; order.AssignedAt ??= Now;
                    db.HistorialOrdenes.Add(new() { OrdenEntregaId = order.Id, TipoEvento = TipoEventoOrden.Asignada,
                        ActorIdentityUserId = a.UserId, ActorRol = "Gestor", CreatedAt = Now, Descripcion = $"Asignada mediante ruta R-{route.Id:000}." });
                }
                route.Estado = EstadoRutaOperativa.Activa; route.IniciadaAt = Now;
            }
            else if (r.Accion == "Finalizar")
            {
                if (route.Estado != EstadoRutaOperativa.Activa || route.Puntos.Any(p => !PointCompleted(p) && !PointExcluded(p))) throw new OperationsException(400, "La ruta todavía tiene entregas pendientes.");
                route.Estado = EstadoRutaOperativa.Finalizada; route.FinalizadaAt = Now;
                foreach (var entry in route.Ordenes) entry.Vigente = false;
            }
            else if (r.Accion == "Cancelar")
            {
                if (route.Estado is EstadoRutaOperativa.Cancelada or EstadoRutaOperativa.Finalizada || route.Ordenes.Any(x => x.Vigente && (x.ParadaEntregaId.HasValue ? x.ParadaEntrega!.RecogidaParadaAt.HasValue || x.ParadaEntrega.PilotoCustodioId.HasValue : x.OrdenEntrega.RecolectadaAt != null))) throw new OperationsException(400, "Una ruta finalizada o con paquetes recogidos requiere atención operativa; no puede cancelarse directamente.");
                foreach (var entry in route.Ordenes)
                {
                    var wasCurrent = entry.Vigente;
                    entry.Vigente = false;
                    if (entry.ParadaEntregaId.HasValue)
                    {
                        var stop = entry.ParadaEntrega!;
                        if (wasCurrent && route.Estado == EstadoRutaOperativa.Activa && !EstadosEntrega.ParadasTerminales.Contains(stop.Estado) && stop.PilotoAsignadoId == route.PilotoId)
                        {
                            stop.PilotoAsignadoId = null; stop.RevisionAsignacion = Guid.NewGuid().ToString("N");
                            ReconcileManagedOrder(entry.OrdenEntrega, Now);
                            db.HistorialOrdenes.Add(new() { OrdenEntregaId = entry.OrdenEntregaId, TipoEvento = TipoEventoOrden.Comentario,
                                ActorIdentityUserId = a.UserId, ActorRol = "Gestor", CreatedAt = Now, Descripcion = $"Ruta R-{id:000} cancelada antes de recoger la parada {stop.Id}; las demás asignaciones se conservan." });
                        }
                        continue;
                    }
                    if (route.Estado == EstadoRutaOperativa.Activa && !EstadosEntrega.Terminales.Contains(entry.OrdenEntrega.Estado))
                    {
                        entry.OrdenEntrega.PilotoId = null; entry.OrdenEntrega.Estado = EstadoOrden.Creada;
                        db.HistorialOrdenes.Add(new() { OrdenEntregaId = entry.OrdenEntregaId, TipoEvento = TipoEventoOrden.Comentario,
                            ActorIdentityUserId = a.UserId, ActorRol = "Gestor", CreatedAt = Now, Descripcion = $"Ruta R-{id:000} cancelada antes de recoger; orden devuelta al despacho." });
                    }
                }
                route.Estado = EstadoRutaOperativa.Cancelada;
            }
            else throw new OperationsException(400, "Acción de ruta no válida.");
            Touch(route); Audit(a, "Ruta", id, r.Accion);
            return new { id, version = route.Version };
        });
    }

    public async Task<object> ReordenarRutaAsync(OperationsActor a, int id, ReordenarRutaRequest r)
        => await MutateAsync(a, "ReordenarRuta", r.OperacionId, new { id, r }, async () =>
        {
            var route = await LoadRouteAsync(a, id); CheckVersion(route, r.Version);
            if (route.Estado is EstadoRutaOperativa.Finalizada or EstadoRutaOperativa.Cancelada || (a.EsPiloto && (!route.PermitirReordenarPiloto || route.Estado != EstadoRutaOperativa.Activa))) throw new OperationsException(403, "No está permitido cambiar esta secuencia.");
            var points = route.Puntos.OrderBy(p => p.Posicion).ToList();
            if (r.PuntoIds is null || r.PuntoIds.Count != points.Count || r.PuntoIds.Distinct().Count() != points.Count || points.Any(p => !r.PuntoIds.Contains(p.Id))) throw new OperationsException(400, "La secuencia debe incluir todos los puntos una sola vez.");
            var done = points.Where(PointCompleted).ToList();
            if (done.Any(p => r.PuntoIds.IndexOf(p.Id) != p.Posicion - 1)) throw new OperationsException(400, "Las visitas completadas conservan su posición.");
            foreach (var group in points.GroupBy(p => p.RutaOrdenId))
            {
                var pickup = group.SingleOrDefault(p => p.Tipo == TipoPuntoRuta.Recogida);
                if (pickup is not null && group.Where(p => p.Tipo != TipoPuntoRuta.Recogida).Any(p => r.PuntoIds.IndexOf(p.Id) < r.PuntoIds.IndexOf(pickup.Id))) throw new OperationsException(400, "Cada recogida debe preceder a sus entregas o retornos.");
            }
            // Avoid transient unique-position collisions while keeping the change atomic.
            foreach (var p in points) p.Posicion = -p.Posicion;
            await db.SaveChangesAsync();
            foreach (var p in points) p.Posicion = r.PuntoIds.IndexOf(p.Id) + 1;
            Touch(route); Audit(a, "Ruta", id, "Secuencia modificada; identidades y códigos conservados.");
            return new { id, version = route.Version };
        });

    public async Task<object> LlegadaAsync(OperationsActor a, int routeId, int pointId, LlegadaPuntoRequest r)
        => await MutateAsync(a, "Llegada", r.OperacionId, new { routeId, pointId, r }, async () =>
        {
            var route = await LoadRouteAsync(a, routeId);
            if (route.Estado != EstadoRutaOperativa.Activa) throw new OperationsException(400, "La ruta no está activa.");
            var next = route.Puntos.OrderBy(p => p.Posicion).FirstOrDefault(p => !PointCompleted(p) && !PointExcluded(p));
            if (next is null || next.Id != pointId) throw new OperationsException(409, "La próxima visita cambió. Actualiza la ruta.");
            next.LlegadaAt ??= Now; Touch(route); Audit(a, "Llegada", pointId, "Llegada registrada; no confirma recogida ni entrega.");
            return new { id = pointId, llegadaAt = next.LlegadaAt, version = route.Version };
        });

    private async Task<RutaOperativa> LoadRouteAsync(OperationsActor a, int id)
        => await db.RutasOperativas.Include(x => x.Ordenes).ThenInclude(x => x.OrdenEntrega).ThenInclude(o => o.Paradas)
            .Include(x => x.Ordenes).ThenInclude(x => x.ParadaEntrega)
            .Include(x => x.Puntos).ThenInclude(x => x.ParadaEntrega).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id && x.ClienteAppId == a.TenantId && (!a.EsPiloto || x.PilotoId == a.PilotoId)) ?? throw new OperationsException(404, "Ruta no disponible.");
    private static bool PointCompleted(PuntoRutaOperativa p) => p.Tipo == TipoPuntoRuta.Recogida
        ? p.RecogidaConfirmadaAt.HasValue || (p.RutaOrden.ParadaEntregaId.HasValue
            ? p.RutaOrden.Vigente && p.ParadaEntrega?.RecogidaParadaAt.HasValue == true && p.ParadaEntrega.PilotoAsignadoId == p.RutaOrden.Ruta.PilotoId
            : p.RutaOrden.OrdenEntrega.RecolectadaAt.HasValue)
        : p.Tipo == TipoPuntoRuta.Devolucion ? p.ParadaEntrega?.Estado == EstadoParadaEntrega.Devuelta
        : p.ParadaEntrega?.Estado == EstadoParadaEntrega.Entregada && (!p.RutaOrden.ParadaEntregaId.HasValue || p.ParadaEntrega.PilotoEntregaId == p.RutaOrden.Ruta.PilotoId);
    private static bool PointExcluded(PuntoRutaOperativa p) => !p.RutaOrden.Vigente || p.RutaOrden.OrdenEntrega.Estado == EstadoOrden.Cancelada || p.ParadaEntrega?.Estado == EstadoParadaEntrega.Cancelada
        || (p.RutaOrden.ParadaEntregaId.HasValue && (p.ParadaEntrega?.EnReasignacion == true
            || (p.ParadaEntrega?.PilotoAsignadoId.HasValue == true && p.ParadaEntrega.PilotoAsignadoId != p.RutaOrden.Ruta.PilotoId)
            || (p.RutaOrden.Ruta.Estado == EstadoRutaOperativa.Activa && p.ParadaEntrega?.PilotoAsignadoId is null)))
        || (p.Tipo == TipoPuntoRuta.Entrega && p.ParadaEntrega?.Estado is EstadoParadaEntrega.DevolucionAutorizada or EstadoParadaEntrega.EnRetorno or EstadoParadaEntrega.Devuelta);
    private static void ValidateManagedRouteStop(ParadaEntrega stop, int? pilotId)
    {
        if (stop.Estado != EstadoParadaEntrega.Pendiente || stop.EnReasignacion || (stop.PilotoAsignadoId.HasValue && stop.PilotoAsignadoId != pilotId)) throw new OperationsException(409, "Una parada cambió de asignación o no está disponible para esta ruta.");
        if (stop.PilotoCustodioId.HasValue && (stop.PilotoCustodioId != pilotId || !stop.RecogidaParadaAt.HasValue)) throw new OperationsException(409, "Confirma primero la recepción del traspaso; la custodia anterior se conserva hasta recibir.");
    }

    // Called inside the tenant transaction when a legacy order starts explicit
    // stop management. Existing visit identities/arrival evidence are retained.
    private async Task ConvertManagedRouteMembershipsAsync(OrdenEntrega order)
    {
        var routes = await db.RutasOperativas.Where(r => r.ClienteAppId == order.ClienteAppId
                && r.Ordenes.Any(m => m.OrdenEntregaId == order.Id && m.Vigente && m.ParadaEntregaId == null))
            .Include(r => r.Ordenes).ThenInclude(m => m.OrdenEntrega).ThenInclude(o => o.Paradas)
            .Include(r => r.Puntos).AsSplitQuery().ToListAsync();
        foreach (var route in routes)
        {
            var old = route.Ordenes.Single(m => m.OrdenEntregaId == order.Id && m.Vigente && m.ParadaEntregaId == null);
            var originalPoints = route.Puntos.OrderBy(p => p.Posicion).ToList();
            var pickup = originalPoints.SingleOrDefault(p => p.RutaOrdenId == old.Id && p.Tipo == TipoPuntoRuta.Recogida)
                ?? throw new OperationsException(409, "La ruta anterior requiere revisar su recogida antes de gestionar las paradas.");
            pickup.RecogidaConfirmadaAt ??= order.RecolectadaAt;
            old.Vigente = false;
            await db.SaveChangesAsync();
            var slots = order.Paradas.OrderBy(p => p.Posicion).Select(stop => new RutaOrdenOperativa
            {
                ClienteAppId = order.ClienteAppId, OrdenEntregaId = order.Id, OrdenEntrega = order,
                RutaId = route.Id, Ruta = route, ParadaEntregaId = stop.Id, ParadaEntrega = stop,
                Vigente = !EstadosEntrega.ParadasTerminales.Contains(stop.Estado)
            }).ToList();
            db.RutaOrdenesOperativas.AddRange(slots);
            await db.SaveChangesAsync();
            var first = slots[0];
            pickup.RutaOrden = first; pickup.ParadaEntregaId = first.ParadaEntregaId; pickup.ParadaEntrega = first.ParadaEntrega;
            foreach (var point in originalPoints.Where(p => p.RutaOrdenId == old.Id && p.Tipo != TipoPuntoRuta.Recogida))
            {
                var slot = slots.SingleOrDefault(m => m.ParadaEntregaId == point.ParadaEntregaId)
                    ?? throw new OperationsException(409, "La ruta anterior contiene una visita sin parada válida.");
                point.RutaOrden = slot;
            }
            var added = new List<PuntoRutaOperativa>();
            if (!order.RecolectadaAt.HasValue)
                added.AddRange(slots.Skip(1).Where(m => m.Vigente).Select(m => new PuntoRutaOperativa
                {
                    ClienteAppId = order.ClienteAppId, RutaId = route.Id, Ruta = route,
                    OrdenEntregaId = order.Id, RutaOrden = m, ParadaEntregaId = m.ParadaEntregaId,
                    ParadaEntrega = m.ParadaEntrega, Tipo = TipoPuntoRuta.Recogida
                }));
            // Free every persisted positive position before inserting new visits.
            foreach (var point in originalPoints) point.Posicion = -Math.Abs(point.Posicion);
            await db.SaveChangesAsync();
            var sequence = new List<PuntoRutaOperativa>();
            foreach (var point in originalPoints)
            {
                sequence.Add(point);
                if (point == pickup) sequence.AddRange(added);
            }
            for (var index = 0; index < sequence.Count; index++) sequence[index].Posicion = index + 1;
            db.PuntosRutaOperativos.AddRange(added);
            Touch(route);
            await db.SaveChangesAsync();
        }
    }

    private async Task ReleaseManagedRouteStopsAsync(int tenant, int orderId, IReadOnlyCollection<int> stopIds)
    {
        var ids = stopIds.ToArray();
        var routes = await db.RutasOperativas.Where(r => r.ClienteAppId == tenant
                && r.Ordenes.Any(m => m.OrdenEntregaId == orderId && m.Vigente && m.ParadaEntregaId.HasValue && ids.Contains(m.ParadaEntregaId.Value)))
            .Include(r => r.Ordenes).ThenInclude(m => m.OrdenEntrega).ThenInclude(o => o.Paradas)
            .Include(r => r.Puntos).ThenInclude(p => p.ParadaEntrega).AsSplitQuery().ToListAsync();
        foreach (var route in routes)
        {
            foreach (var point in route.Puntos.Where(p => p.OrdenEntregaId == orderId && p.Tipo == TipoPuntoRuta.Recogida && p.ParadaEntregaId.HasValue && ids.Contains(p.ParadaEntregaId.Value)))
                point.RecogidaConfirmadaAt ??= point.ParadaEntrega?.RecogidaParadaAt;
            foreach (var member in route.Ordenes.Where(m => m.OrdenEntregaId == orderId && m.ParadaEntregaId.HasValue && ids.Contains(m.ParadaEntregaId.Value))) member.Vigente = false;
            if (route.Estado is EstadoRutaOperativa.Activa or EstadoRutaOperativa.Planificada && route.Puntos.All(p => PointCompleted(p) || PointExcluded(p)))
            {
                route.Estado = route.Estado == EstadoRutaOperativa.Planificada ? EstadoRutaOperativa.Cancelada : EstadoRutaOperativa.Finalizada;
                route.FinalizadaAt = Now;
                foreach (var member in route.Ordenes) member.Vigente = false;
            }
            Touch(route);
        }
        await db.SaveChangesAsync();
    }
    private async Task AvailablePilotAsync(OperationsActor a, int id)
    { if (!await db.Pilotos.AnyAsync(p => p.Id == id && p.ClienteAppId == a.TenantId && p.Activo)) throw new OperationsException(400, "El piloto no está activo en esta empresa."); }
}
