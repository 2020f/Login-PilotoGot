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
            if (r.OrdenIds is null || r.OrdenIds.Count is < 1 or > 100 || r.OrdenIds.Distinct().Count() != r.OrdenIds.Count) throw new OperationsException(400, "Selecciona de una a cien órdenes distintas.");
            if (r.Fecha < Today.AddDays(-1) || r.Fecha > Today.AddYears(1)) throw new OperationsException(400, "La fecha de planificación está fuera del intervalo permitido.");
            if (r.ZonaId.HasValue && !await db.ZonasOperativas.AnyAsync(z => z.Id == r.ZonaId && z.ClienteAppId == a.TenantId && z.Activa)) throw new OperationsException(400, "Zona no disponible.");
            if (r.PilotoId.HasValue) await AvailablePilotAsync(a, r.PilotoId.Value);
            var orders = await db.OrdenesEntrega.Include(o => o.Paradas).Where(o => r.OrdenIds.Contains(o.Id) && o.ClienteAppId == a.TenantId).ToListAsync();
            if (orders.Count != r.OrdenIds.Count || orders.Any(o => o.Estado is not (EstadoOrden.Creada or EstadoOrden.Asignada) || (o.PilotoId.HasValue && o.PilotoId != r.PilotoId))) throw new OperationsException(400, "Todas las órdenes deben estar disponibles y pertenecer al mismo piloto o estar sin asignar.");
            if (await db.RutaOrdenesOperativas.AnyAsync(x => x.ClienteAppId == a.TenantId && x.Vigente && r.OrdenIds.Contains(x.OrdenEntregaId))) throw new OperationsException(409, "Una orden seleccionada ya está incluida en otro recorrido.");
            if (await db.DatosEnviosOperativos.AnyAsync(d => d.ClienteAppId == a.TenantId && r.OrdenIds.Contains(d.OrdenEntregaId) && d.CanceladaAt != null)) throw new OperationsException(400, "La selección contiene una orden cancelada.");
            if (orders.Any(o => o.Paradas.Count == 0) || orders.Sum(o => o.Paradas.Count + 1) > 500) throw new OperationsException(400, "La ruta requiere paradas migradas y admite hasta 500 puntos.");
            var route = new RutaOperativa { ClienteAppId = a.TenantId, Nombre = Required(r.Nombre, 120, "Nombre"), Fecha = r.Fecha,
                ZonaId = r.ZonaId, PilotoId = r.PilotoId, PermitirReordenarPiloto = r.PermitirReordenarPiloto, CreatedAt = Now, UpdatedAt = Now };
            foreach (var orderId in r.OrdenIds) route.Ordenes.Add(new() { ClienteAppId = a.TenantId, OrdenEntregaId = orderId });
            db.RutasOperativas.Add(route); await db.SaveChangesAsync();
            var position = 1;
            // The planner exposes this initial sequence; subsequent changes are explicit commands.
            foreach (var id in r.OrdenIds) route.Puntos.Add(new() { ClienteAppId = a.TenantId, RutaId = route.Id,
                OrdenEntregaId = id, RutaOrden = route.Ordenes.Single(x => x.OrdenEntregaId == id), Tipo = TipoPuntoRuta.Recogida, Posicion = position++ });
            foreach (var id in r.OrdenIds) foreach (var stop in orders.Single(o => o.Id == id).Paradas.OrderBy(p => p.Posicion))
                route.Puntos.Add(new() { ClienteAppId = a.TenantId, RutaId = route.Id, OrdenEntregaId = id,
                    RutaOrden = route.Ordenes.Single(x => x.OrdenEntregaId == id), ParadaEntregaId = stop.Id, Tipo = TipoPuntoRuta.Entrega, Posicion = position++ });
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
            if (route.Ordenes.Any(x => x.Vigente && !EstadosEntrega.Terminales.Contains(x.OrdenEntrega.Estado) && x.OrdenEntrega.PilotoId.HasValue && x.OrdenEntrega.PilotoId != r.PilotoId)) throw new OperationsException(409, "Una orden ya tiene otro piloto. Revisa su asignación antes de continuar.");
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
                if (route.Estado != EstadoRutaOperativa.Activa || route.Ordenes.Any(x => !EstadosEntrega.Terminales.Contains(x.OrdenEntrega.Estado))) throw new OperationsException(400, "La ruta todavía tiene entregas pendientes.");
                route.Estado = EstadoRutaOperativa.Finalizada; route.FinalizadaAt = Now;
                foreach (var entry in route.Ordenes) entry.Vigente = false;
            }
            else if (r.Accion == "Cancelar")
            {
                if (route.Estado is EstadoRutaOperativa.Cancelada or EstadoRutaOperativa.Finalizada || route.Ordenes.Any(x => x.OrdenEntrega.RecolectadaAt != null)) throw new OperationsException(400, "Una ruta finalizada o con paquetes recogidos requiere atención operativa; no puede cancelarse directamente.");
                foreach (var entry in route.Ordenes)
                {
                    entry.Vigente = false;
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
            foreach (var group in points.GroupBy(p => p.OrdenEntregaId))
            {
                var pickup = group.Single(p => p.Tipo == TipoPuntoRuta.Recogida);
                if (group.Where(p => p.Tipo != TipoPuntoRuta.Recogida).Any(p => r.PuntoIds.IndexOf(p.Id) < r.PuntoIds.IndexOf(pickup.Id))) throw new OperationsException(400, "Cada recogida debe preceder a sus entregas o retornos.");
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
            .Include(x => x.Puntos).ThenInclude(x => x.ParadaEntrega).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id && x.ClienteAppId == a.TenantId && (!a.EsPiloto || x.PilotoId == a.PilotoId)) ?? throw new OperationsException(404, "Ruta no disponible.");
    private static bool PointCompleted(PuntoRutaOperativa p) => p.Tipo == TipoPuntoRuta.Recogida ? p.RutaOrden.OrdenEntrega.RecolectadaAt.HasValue : p.Tipo == TipoPuntoRuta.Devolucion ? p.ParadaEntrega?.Estado == EstadoParadaEntrega.Devuelta : p.ParadaEntrega?.Estado == EstadoParadaEntrega.Entregada;
    private static bool PointExcluded(PuntoRutaOperativa p) => p.RutaOrden.OrdenEntrega.Estado == EstadoOrden.Cancelada || p.ParadaEntrega?.Estado == EstadoParadaEntrega.Cancelada
        || (p.Tipo == TipoPuntoRuta.Entrega && p.ParadaEntrega?.Estado is EstadoParadaEntrega.DevolucionAutorizada or EstadoParadaEntrega.EnRetorno or EstadoParadaEntrega.Devuelta);
    private async Task AvailablePilotAsync(OperationsActor a, int id)
    { if (!await db.Pilotos.AnyAsync(p => p.Id == id && p.ClienteAppId == a.TenantId && p.Activo)) throw new OperationsException(400, "El piloto no está activo en esta empresa."); }
}
