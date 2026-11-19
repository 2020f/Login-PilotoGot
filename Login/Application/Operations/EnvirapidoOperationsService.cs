using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService(ApplicationDbContext db, QuotaPolicyService quotas,
    TimeProvider clock, IConfiguration configuration, RecorridosService recorridos)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private TimeZoneInfo BusinessZone
    {
        get
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(configuration["Envirapido:ZonaHoraria"] ?? "America/Santo_Domingo"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time"); }
        }
    }
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, BusinessZone));
    private (DateTime? From, DateTime? Until) ReadPeriod(DateOnly? desde, DateOnly? hasta)
    {
        if (desde.HasValue && hasta.HasValue && (desde > hasta || hasta.Value.DayNumber - desde.Value.DayNumber > 3660)
            || hasta == DateOnly.MaxValue) throw new OperationsException(400, "Selecciona un período válido de hasta diez años.");
        return (desde.HasValue ? TimeZoneInfo.ConvertTimeToUtc(desde.Value.ToDateTime(TimeOnly.MinValue), BusinessZone) : null,
            hasta.HasValue ? TimeZoneInfo.ConvertTimeToUtc(hasta.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessZone) : null);
    }

    public async Task<OperationsActor> ManagerAsync(string userId)
    {
        var tenant = await db.ClientesApp.AsNoTracking().Where(x => x.GestorIdentityUserId == userId && x.Estado == EstadoClienteApp.Activo)
            .Select(x => x.Id).SingleOrDefaultAsync();
        if (tenant == 0 || string.IsNullOrWhiteSpace(userId)) throw new OperationsException(403, "No tienes acceso al centro de operaciones de esta empresa.");
        return new(tenant, userId);
    }
    public async Task<OperationsActor> PilotAsync(string userId)
    {
        var p = await db.Pilotos.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Activo && x.ClienteApp.Estado == EstadoClienteApp.Activo)
            .Select(x => new { x.Id, x.ClienteAppId }).SingleOrDefaultAsync();
        if (p is null) throw new OperationsException(403, "Piloto no disponible.");
        return new(p.ClienteAppId, userId, p.Id);
    }
    private async Task EnsureActorAsync(OperationsActor a, bool manager = false)
    {
        if (manager && (a.EsPiloto || a.TiendaReceptoraId.HasValue)) throw new OperationsException(403, "Esta acción requiere un gestor autorizado.");
        var valid = a.EsPiloto
            ? await db.Pilotos.AnyAsync(p => p.Id == a.PilotoId && p.ClienteAppId == a.TenantId && p.Activo && p.IdentityUserId == a.UserId && p.ClienteApp.Estado == EstadoClienteApp.Activo)
            : a.TiendaReceptoraId.HasValue
                ? await db.UsuariosTienda.AnyAsync(u => u.IdentityUserId == a.UserId && u.Activo
                    && u.TiendaId == a.TiendaReceptoraId && u.Tienda.Activo && u.Tienda.ClienteAppId == a.TenantId && u.Tienda.ClienteApp.Estado == EstadoClienteApp.Activo)
                : await db.ClientesApp.AnyAsync(t => t.Id == a.TenantId && t.GestorIdentityUserId == a.UserId && t.Estado == EstadoClienteApp.Activo);
        if (!valid) throw new OperationsException(403, "El acceso a la empresa cambió. Actualiza la sesión.");
    }
    private void Audit(OperationsActor a, string tipo, int id, string text)
        => db.EventosOperativos.Add(new() { ClienteAppId = a.TenantId, ActorId = a.UserId, Tipo = tipo,
            RecursoId = id, Descripcion = text, CreatedAt = Now });
    private void Touch(OperativoTenant entity) { entity.Version = Guid.NewGuid().ToString("N"); entity.UpdatedAt = Now; }
    private static void CheckVersion(OperativoTenant entity, string? version)
    { if (string.IsNullOrEmpty(version) || entity.Version != version) throw new OperationsException(409, "El registro cambió. Actualízalo antes de continuar."); }
    private static string Required(string? text, int max, string label)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > max) throw new OperationsException(400, $"{label} es obligatorio y admite hasta {max} caracteres.");
        return text;
    }
    private static string? Optional(string? text, int max, string label)
    { text = text?.Trim(); if (text?.Length > max) throw new OperationsException(400, $"{label} admite hasta {max} caracteres."); return string.IsNullOrEmpty(text) ? null : text; }
    private static T EnumValue<T>(string value) where T : struct, Enum
    { if (!Enum.TryParse<T>(value, false, out var result) || !Enum.IsDefined(result)) throw new OperationsException(400, "La opción seleccionada no es válida."); return result; }

    // The result is stored in the same transaction as the mutation. A lost response
    // replays the ACK, whereas a key with another payload is an explicit conflict.
    private async Task<object> MutateAsync(OperationsActor a, string tipo, string? key, object payload, Func<Task<object>> apply)
    {
        if (!Guid.TryParse(key, out var guid)) throw new OperationsException(400, "La operación requiere un identificador válido.");
        var normalized = guid.ToString("N");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json))));
        await using var tx = await quotas.BeginTenantMutationAsync(a.TenantId);
        await EnsureActorAsync(a);
        var prior = await db.OperacionesEnvirapido.AsNoTracking().SingleOrDefaultAsync(x => x.ClienteAppId == a.TenantId && x.ActorId == a.UserId && x.Tipo == tipo && x.Clave == normalized);
        if (prior is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(prior.PayloadHash), Encoding.ASCII.GetBytes(hash)))
                throw new OperationsException(409, "Esta operación ya existe con datos diferentes. Recupera su resultado antes de modificarla.");
            return JsonSerializer.Deserialize<JsonElement>(prior.ResultadoJson);
        }
        try
        {
            var result = await apply();
            db.OperacionesEnvirapido.Add(new() { ClienteAppId = a.TenantId, ActorId = a.UserId,
                Tipo = tipo, Clave = normalized, PayloadHash = hash, ResultadoJson = JsonSerializer.Serialize(result, Json), CreatedAt = Now });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return result;
        }
        catch (OperationsException e) when (e.StatusCode is 400 or 409)
        {
            await tx.RollbackAsync(); db.ChangeTracker.Clear();
            throw new RejectedOperationException(e.Message, e.StatusCode);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(); db.ChangeTracker.Clear();
            throw new RejectedOperationException("La asignación cambió. Actualiza el registro antes de continuar.", 409);
        }
    }

    private async Task<OrdenEntrega> OrderAsync(OperationsActor a, int id)
    {
        var order = await db.OrdenesEntrega.Include(o => o.Paradas).SingleOrDefaultAsync(o => o.Id == id && o.ClienteAppId == a.TenantId
            && (!a.EsPiloto || !o.GestionPorParadas && o.PilotoId == a.PilotoId
                || o.GestionPorParadas && o.Paradas.Any(p => p.PilotoAsignadoId == a.PilotoId && !p.EnReasignacion)));
        return order ?? throw new OperationsException(404, "Orden no disponible.");
    }

    public async Task<OperationsSnapshot> SnapshotAsync(OperationsActor a, DateOnly? desde = null, DateOnly? hasta = null)
    {
        await EnsureActorAsync(a);
        var tenant = a.TenantId;
        var (from, until) = ReadPeriod(desde, hasta);
        var orders = await db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == tenant && (!a.EsPiloto
                || (!o.GestionPorParadas && o.PilotoId == a.PilotoId)
                || (o.GestionPorParadas && o.Paradas.Any(p => p.PilotoAsignadoId == a.PilotoId || p.PilotoCustodioId == a.PilotoId || p.PilotoEntregaId == a.PilotoId))))
            .Include(o => o.Paradas).ToListAsync();
        var routeQuery = db.RutasOperativas.AsNoTrackingWithIdentityResolution().Where(r => r.ClienteAppId == tenant && (!a.EsPiloto || r.PilotoId == a.PilotoId)
            && (!desde.HasValue || r.Fecha >= desde.Value) && (!hasta.HasValue || r.Fecha <= hasta.Value));
        var totalRoutes = await routeQuery.CountAsync();
        var activeRoutes = await routeQuery.CountAsync(r => r.Estado == EstadoRutaOperativa.Activa || r.Estado == EstadoRutaOperativa.Planificada);
        var routes = await routeQuery.Include(r => r.Piloto).Include(r => r.Zona).Include(r => r.Ordenes).ThenInclude(x => x.OrdenEntrega).ThenInclude(o => o.Paradas)
            .Include(r => r.Ordenes).ThenInclude(x => x.ParadaEntrega)
            .Include(r => r.Puntos).ThenInclude(p => p.ParadaEntrega).Include(r => r.Puntos).ThenInclude(p => p.RutaOrden)
            .OrderBy(r => r.Estado == EstadoRutaOperativa.Finalizada || r.Estado == EstadoRutaOperativa.Cancelada)
            .ThenByDescending(r => r.Fecha).ThenByDescending(r => r.Id).Take(Math.Max(300, activeRoutes)).AsSplitQuery().ToListAsync();
        var orderIds = orders.Select(o => o.Id).ToArray();
        var routeIds = routes.Select(r => r.Id).ToArray();
        var ownedStopIds = orders.Where(o => o.GestionPorParadas).SelectMany(o => o.Paradas)
            .Where(p => p.PilotoAsignadoId == a.PilotoId || p.PilotoCustodioId == a.PilotoId || p.PilotoEntregaId == a.PilotoId).Select(p => p.Id).ToArray();
        var incidentQuery = db.IncidenciasOperativas.AsNoTracking().Where(i => i.ClienteAppId == tenant && (!a.EsPiloto || i.ReportadaPor == a.UserId
                || (i.OrdenEntregaId != null && orderIds.Contains(i.OrdenEntregaId.Value) && (!i.ParadaEntregaId.HasValue || ownedStopIds.Contains(i.ParadaEntregaId.Value)
                    || !db.OrdenesEntrega.Any(o => o.Id == i.OrdenEntregaId && o.GestionPorParadas)))
                || (i.RutaId != null && routeIds.Contains(i.RutaId.Value) && !i.ParadaEntregaId.HasValue))
            && (!from.HasValue || i.CreatedAt >= from.Value) && (!until.HasValue || i.CreatedAt < until.Value));
        var totalIncidents = await incidentQuery.CountAsync(); var openIncidents = await incidentQuery.CountAsync(i => i.Estado != EstadoIncidencia.Resuelta);
        var incidents = await incidentQuery.Include(i => i.Actividad).OrderBy(i => i.Estado == EstadoIncidencia.Resuelta)
            .ThenByDescending(i => i.Prioridad).ThenBy(i => i.VenceAt).ThenByDescending(i => i.CreatedAt).Take(Math.Max(1000, openIncidents)).ToListAsync();
        var managerId = await db.ClientesApp.Where(x => x.Id == tenant).Select(x => x.GestorIdentityUserId).SingleAsync();
        var names = await db.Users.AsNoTracking().Where(u => u.Id == managerId || u.Id == a.UserId || db.Pilotos.Any(p => p.ClienteAppId == tenant && p.IdentityUserId == u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? u.Id);
        var cashQuery = db.CobrosOperativos.AsNoTracking().Where(c => c.ClienteAppId == tenant && (!a.EsPiloto || c.PilotoId == a.PilotoId)
            && (!from.HasValue || c.CreatedAt >= from.Value) && (!until.HasValue || c.CreatedAt < until.Value));
        var totalCash = await cashQuery.CountAsync(); var pendingCash = await cashQuery.CountAsync(c => c.EntregadoEmpresaAt == null);
        var cash = await cashQuery.OrderBy(c => c.EntregadoEmpresaAt != null).ThenByDescending(c => c.CreatedAt).Take(Math.Max(2000, pendingCash)).ToListAsync();
        var settlementQuery = db.LiquidacionesOperativas.AsNoTracking().Where(l => l.ClienteAppId == tenant && (!a.EsPiloto || l.PilotoId == a.PilotoId)
                && (!desde.HasValue || l.Hasta >= desde.Value) && (!hasta.HasValue || l.Desde <= hasta.Value));
        var totalSettlements = await settlementQuery.CountAsync(); var unpaidSettlements = await settlementQuery.CountAsync(l => l.Estado != EstadoLiquidacion.Pagada);
        var settlements = await settlementQuery.Include(l => l.Piloto).Include(l => l.Tienda).Include(l => l.Movimientos)
            .OrderBy(l => l.Estado == EstadoLiquidacion.Pagada).ThenByDescending(l => l.CreatedAt).Take(Math.Max(500, unpaidSettlements)).AsSplitQuery().ToListAsync();
        var zones = await db.ZonasOperativas.AsNoTracking().Where(z => z.ClienteAppId == tenant && (!a.EsPiloto || routes.Select(r => r.ZonaId).Contains(z.Id))).ToListAsync();
        var pilotPositions = await db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == tenant && p.Activo).ToListAsync();
        var routeDtos = new List<RutaOperativaDto>();
        foreach (var r in routes) routeDtos.Add(await RouteDtoAsync(r, a));
        var configs = await db.ReglasIncidencia.AsNoTracking().Where(r => r.ClienteAppId == tenant).ToListAsync();
        var financeRules = a.EsPiloto ? [] : await db.ReglasRemuneracion.AsNoTracking().Where(r => r.ClienteAppId == tenant).ToListAsync();
        var data = await db.DatosEnviosOperativos.AsNoTracking().Where(d => d.ClienteAppId == tenant && (!a.EsPiloto || orderIds.Contains(d.OrdenEntregaId))).ToListAsync();
        var active = await TieneActividadGpsAsync(a);
        var lease = Math.Clamp(configuration.GetValue<int?>("Envirapido:GpsAutorizacionSegundos") ?? 120, 60, 900);
        var events = a.EsPiloto ? [] : await db.EventosOperativos.AsNoTracking().Where(e => e.ClienteAppId == tenant
                && (!from.HasValue || e.CreatedAt >= from.Value) && (!until.HasValue || e.CreatedAt < until.Value))
            .OrderByDescending(e => e.Id).Take(2000).ToListAsync();
        var indicators = await IndicadoresAsync(a, desde, hasta);
        return new(Now, active, zones.Select(z => ZoneDto(z, orders, pilotPositions, data)).ToList(), routeDtos,
            incidents.Select(i => IncidentDto(i, names, a, orders, routes)).ToList(), configs.Select(RuleDto).ToList(), financeRules.Select(PaymentRuleDto).ToList(),
            settlements.Select(SettlementDto).ToList(), cash.Select(CashDto).ToList(), data.Select(d => DataDto(d, a, orders)).ToList(),
            a.EsPiloto || managerId is null ? [] : [new(managerId, names.GetValueOrDefault(managerId, "Gestor"))], active ? Now.AddSeconds(lease) : null, lease,
            events.Select(e => new EventoOperativoDto(e.Id, e.ActorId == "System" ? "Sistema" : names.GetValueOrDefault(e.ActorId, "Gestor"), e.Tipo, e.RecursoId, e.Descripcion, e.CreatedAt)).ToList(),
            OperationsGpsPolicy.FreshnessSeconds(configuration), new(routes.Count, totalRoutes, incidents.Count, totalIncidents,
                settlements.Count, totalSettlements, cash.Count, totalCash), indicators);
    }

    private ZonaOperativaDto ZoneDto(ZonaOperativa z, List<OrdenEntrega> orders, List<Piloto> pilots, List<DatosEnvioOperativos> data)
    {
        var polygon = JsonSerializer.Deserialize<List<GeoPoint>>(z.PoligonoJson, Json) ?? [];
        var explicitIds = data.Where(d => d.ZonaId == z.Id).Select(d => d.OrdenEntregaId).ToHashSet();
        var futureIds = data.Where(d => d.ProgramadaPara > Now).Select(d => d.OrdenEntregaId).ToHashSet();
        var selected = orders.Where(o => !(o.Estado == EstadoOrden.Creada && futureIds.Contains(o.Id))
            && (explicitIds.Contains(o.Id) || o.Paradas.Any(p => p.Latitud.HasValue && p.Longitud.HasValue && GeoGeometry.Contains(polygon, new(p.Latitud.Value, p.Longitud.Value))))).ToList();
        var load = z.UnidadCapacidad == "OrdenesAbiertas" ? selected.Count(o => !EstadosEntrega.Terminales.Contains(o.Estado))
            : selected.SelectMany(o => o.Paradas).Count(p => !EstadosEntrega.ParadasTerminales.Contains(p.Estado));
        var available = pilots.Count(p => p.EnLinea && p.Latitud.HasValue && p.Longitud.HasValue && p.UltimaUbicacionAt > Now.AddSeconds(-OperationsGpsPolicy.FreshnessSeconds(configuration))
            && GeoGeometry.Contains(polygon, new(p.Latitud.Value, p.Longitud.Value)) && !orders.Any(o => o.GestionPorParadas
                ? o.Paradas.Any(stop => (!EstadosEntrega.ParadasTerminales.Contains(stop.Estado) && stop.PilotoAsignadoId == p.Id)
                    || (stop.PilotoCustodioId == p.Id && stop.Estado is not (EstadoParadaEntrega.Entregada or EstadoParadaEntrega.Devuelta)))
                : o.PilotoId == p.Id && !EstadosEntrega.Terminales.Contains(o.Estado)));
        return new(z.Id, $"Z-{z.Id:000}", z.Nombre, z.Region, z.Color, polygon, z.SuperficieKm2, z.Activa,
            z.HoraInicio, z.HoraFin, z.Capacidad, z.UnidadCapacidad, z.AvisoPorcentaje, load,
            selected.Count(o => o.Estado == EstadoOrden.Creada), available, z.Capacidad > 0 && 100d * load / z.Capacidad >= z.AvisoPorcentaje, z.Version);
    }
    private async Task<RutaOperativaDto> RouteDtoAsync(RutaOperativa r, OperationsActor a)
    {
        var orderMap = r.Ordenes.GroupBy(x => x.OrdenEntregaId).ToDictionary(x => x.Key, x => x.First().OrdenEntrega);
        var points = r.Puntos.OrderBy(p => p.Posicion).Select(p =>
        {
            var o = orderMap[p.OrdenEntregaId];
            var pickup = p.Tipo is TipoPuntoRuta.Recogida or TipoPuntoRuta.Devolucion;
            var stop = p.ParadaEntrega;
            var managed = o.GestionPorParadas && p.RutaOrden.ParadaEntregaId.HasValue;
            var allowedOrder = !a.EsPiloto || (managed
                ? stop is not null && !stop.EnReasignacion && (stop.PilotoAsignadoId == a.PilotoId || stop.PilotoEntregaId == a.PilotoId
                    || (r.Estado == EstadoRutaOperativa.Planificada && !stop.PilotoAsignadoId.HasValue))
                : o.PilotoId == a.PilotoId || (r.Estado == EstadoRutaOperativa.Planificada && o.PilotoId is null));
            var received = managed ? stop?.RecogidaParadaAt.HasValue == true && stop.PilotoCustodioId == a.PilotoId : o.RecolectadaAt.HasValue;
            var visible = allowedOrder && (!a.EsPiloto || pickup || received || stop?.PilotoEntregaId == a.PilotoId);
            var pickupName = managed ? stop?.RecogidaActualNombre ?? o.RecogidaNombre : o.RecogidaNombre;
            var pickupAddress = managed ? stop?.RecogidaActualDireccion ?? o.RecogidaDireccion : o.RecogidaDireccion;
            var pickupLatitude = managed ? stop?.RecogidaActualLatitud ?? o.RecogidaLatitud : o.RecogidaLatitud;
            var pickupLongitude = managed ? stop?.RecogidaActualLongitud ?? o.RecogidaLongitud : o.RecogidaLongitud;
            return new PuntoRutaDto(p.Id, o.Id, p.ParadaEntregaId, p.Tipo.ToString(), p.Posicion,
                !allowedOrder ? "Parada retirada del recorrido" : pickup ? pickupName : visible ? stop?.Nombre ?? "Entrega" : "Destino disponible después de recoger",
                visible ? pickup ? pickupAddress : stop?.Direccion : null,
                visible ? pickup ? pickupLatitude : stop?.Latitud : null,
                visible ? pickup ? pickupLongitude : stop?.Longitud : null,
                PointCompleted(p) && (allowedOrder || managed && p.Tipo == TipoPuntoRuta.Recogida && p.RecogidaConfirmadaAt.HasValue),
                visible, p.LlegadaAt, p.Tipo == TipoPuntoRuta.Entrega && stop?.Estado is EstadoParadaEntrega.DevolucionAutorizada or EstadoParadaEntrega.EnRetorno or EstadoParadaEntrega.Devuelta,
                allowedOrder ? stop?.Estado.ToString() : "Retirada", !allowedOrder || o.Estado == EstadoOrden.Cancelada || stop?.Estado == EstadoParadaEntrega.Cancelada
                    || (managed && (!p.RutaOrden.Vigente && !PointCompleted(p) || stop?.EnReasignacion == true)),
                managed, allowedOrder ? stop?.RevisionAsignacion : null);
        }).ToList();
        // Only an entirely authorized projection may contain route geometry.
        var hasHidden = points.Any(p => !p.ExcluidoPorCancelacion && !p.PuedeVerDestino);
        var remaining = points.Where(p => !p.Completado && !p.ExcluidoPorRetorno && !p.ExcluidoPorCancelacion).ToList();
        RecorridoEstimado? estimate = null;
        if (!hasHidden && remaining.Count > 0 && r.Estado == EstadoRutaOperativa.Activa)
            estimate = await recorridos.EstimarAsync(r.Id, r.Version, r.Piloto?.Latitud, r.Piloto?.Longitud, r.Piloto?.UltimaUbicacionAt, remaining);
        var estado = r.Estado == EstadoRutaOperativa.Activa ? remaining.Count == 0 ? "Finalizada" : remaining.Any(p => p.Tipo == "Recogida") ? "Recogiendo" : remaining.All(p => p.Tipo == "Devolucion") ? "EnRetorno" : "EnRuta" : r.Estado.ToString();
        return new(r.Id, $"R-{r.Id:000}", r.Nombre, r.Fecha, r.ZonaId, r.Zona?.Nombre, r.PilotoId, r.Piloto?.Nombre,
            estado, r.PermitirReordenarPiloto, points.Count(p => p.Tipo == "Recogida" && p.Completado), points.Count(p => p.Tipo == "Recogida"),
            points.Count(p => p.Tipo == "Entrega" && p.Completado), points.Count(p => p.Tipo == "Entrega"), estimate?.DistanciaKm,
            estimate?.Minutos, estimate is null ? null : estimate.CalculadoAt.AddMinutes(estimate.Minutos), estimate?.Geometria,
            estimate?.CalculadoAt, r.Ordenes.Select(x => x.OrdenEntregaId).Distinct().ToList(), points, r.Version, points.Count(p => p.Tipo == "Entrega" && p.EstadoEntrega == "Devuelta"),
            r.Ordenes.Where(x => x.ParadaEntregaId.HasValue && (!a.EsPiloto || points.Any(p => p.ParadaId == x.ParadaEntregaId && p.PuedeVerDestino))).Select(x => x.ParadaEntregaId!.Value).Distinct().ToList());
    }
    private IncidenciaOperativaDto IncidentDto(IncidenciaOperativa i, Dictionary<string, string> names, OperationsActor actor, List<OrdenEntrega> orders, List<RutaOperativa> routes)
    {
        var visible = !actor.EsPiloto || (i.OrdenEntregaId.HasValue
            ? orders.Any(o => o.Id == i.OrdenEntregaId && (o.GestionPorParadas
                ? i.ParadaEntregaId.HasValue && o.Paradas.Any(p => p.Id == i.ParadaEntregaId && p.PilotoAsignadoId == actor.PilotoId && p.PilotoCustodioId == actor.PilotoId && p.RecogidaParadaAt.HasValue && !p.EnReasignacion)
                : o.PilotoId == actor.PilotoId && o.RecolectadaAt.HasValue))
            : i.RutaId.HasValue && routes.Any(r => r.Id == i.RutaId && r.Ordenes.All(x => x.ParadaEntregaId.HasValue
                ? x.ParadaEntrega is not null && x.ParadaEntrega.PilotoAsignadoId == actor.PilotoId && x.ParadaEntrega.PilotoCustodioId == actor.PilotoId && x.ParadaEntrega.RecogidaParadaAt.HasValue && !x.ParadaEntrega.EnReasignacion
                : x.OrdenEntrega.PilotoId == actor.PilotoId && x.OrdenEntrega.RecolectadaAt.HasValue)));
        return new(i.Id, $"INC-{i.Id:0000}", i.OrdenEntregaId, i.ParadaEntregaId, i.RutaId, i.Categoria,
            visible || i.ReportadaPor == actor.UserId ? i.Descripcion : "Detalle disponible después de recoger la orden asignada.",
            i.Prioridad, i.Estado.ToString(), i.Efecto.ToString(), i.ResponsableId, i.ResponsableId is null ? null : names.GetValueOrDefault(i.ResponsableId, "Responsable"),
            i.VenceAt, i.ResueltaAt, visible ? i.Resolucion : null, i.Estado != EstadoIncidencia.Resuelta && i.VenceAt < Now, i.CreatedAt,
            i.Actividad.Where(x => visible || x.ActorId == actor.UserId).OrderByDescending(x => x.CreatedAt).Select(x => new ActividadIncidenciaDto(x.Id, x.ActorId == "System" ? "Sistema" : names.GetValueOrDefault(x.ActorId, "Operador"), x.Accion, x.Texto, x.CreatedAt)).ToList(), i.Version);
    }
    private static ReglaIncidenciaDto RuleDto(ReglaIncidenciaOperativa r) => new(r.Id, r.Categoria, r.Prioridad, r.MinutosResolucion, r.Efecto.ToString(), r.Activa, r.DetectarDemora, r.Version);
    private static ReglaRemuneracionDto PaymentRuleDto(ReglaRemuneracionOperativa r) => new(r.Id, r.Nombre, r.Unidad.ToString(), r.ImporteFijo, r.PorcentajeTarifa, r.IncentivoFijo, r.ComisionPorcentaje, r.Activa, r.Version);
    private static DatosEnvioDto DataDto(DatosEnvioOperativos d, OperationsActor actor, List<OrdenEntrega> orders) => new(d.OrdenEntregaId, d.Referencia, d.Servicio, d.Paquetes, d.PesoKg, d.Prioridad, d.ZonaId, d.ProgramadaPara, d.CompromisoEntrega, d.CanceladaAt,
        !actor.EsPiloto || orders.Any(o => o.Id == d.OrdenEntregaId && !o.GestionPorParadas && o.PilotoId == actor.PilotoId && o.RecolectadaAt.HasValue) ? d.NotasOperativas : null, d.Version);
    private static CobroOperativoDto CashDto(CobroOperativo c) => new(c.Id, c.OrdenEntregaId, c.ParadaEntregaId, c.PilotoId, c.TiendaId, c.Importe, c.ImporteVerificado, c.Referencia, c.Nota, c.ConciliadoAt, c.LiquidacionId, c.CreatedAt, c.Version, c.EntregadoEmpresaAt, c.ImporteEntregadoEmpresa, c.ReferenciaEntregaEmpresa);
    private static LiquidacionOperativaDto SettlementDto(LiquidacionOperativa l) => new(l.Id, $"LQ-{l.Id:0000}", l.PilotoId, l.TiendaId,
        l.Piloto?.Nombre ?? l.Tienda?.Nombre ?? "Beneficiario", l.Desde, l.Hasta, l.Estado.ToString(), l.Moneda, l.Entregas,
        l.Servicios, l.Incentivos, l.Comisiones, l.Ajustes, l.EfectivoCompensado, l.Neto, l.Conciliada, l.MetodoPago,
        l.CuentaUltimos4 is null ? null : "•••• " + l.CuentaUltimos4, l.FechaProgramada, l.PagadaAt, l.ReferenciaPago,
        l.Movimientos.Where(m => m.Concepto != "Cobertura").Select(m => new MovimientoOperativoDto(m.Id, m.Concepto, m.Descripcion, m.Importe, m.OrdenEntregaId, m.ParadaEntregaId, m.RutaId, m.CreatedAt)).ToList(), l.Version, l.FechaPago);
}

public sealed class RejectedOperationException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
