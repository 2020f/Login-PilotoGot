using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    public async Task<OperationsActor> ReceptorComercioAsync(string userId, int tiendaId)
    {
        var tenant = await db.UsuariosTienda.AsNoTracking().Where(u => u.IdentityUserId == userId && u.TiendaId == tiendaId
            && u.Activo && u.Tienda.Activo && u.Tienda.ClienteApp.Estado == EstadoClienteApp.Activo)
            .Select(u => u.Tienda.ClienteAppId).SingleOrDefaultAsync();
        if (tenant == 0) throw new OperationsException(403, "Comercio receptor no autorizado.");
        return new(tenant, userId, TiendaReceptoraId: tiendaId);
    }
    public async Task<ReasignacionSnapshot> ReasignacionesAsync(OperationsActor a, int? ordenId = null)
    {
        await EnsureActorAsync(a);
        var requests = await db.RetirosParadas.AsNoTracking().Include(x => x.ParadaEntrega).Include(x => x.OrdenEntrega)
            .Where(x => x.ClienteAppId == a.TenantId && (!ordenId.HasValue || x.OrdenEntregaId == ordenId)
                && (!a.EsPiloto || x.PilotoOrigenId == a.PilotoId || x.PilotoDestinoId == a.PilotoId)
                && (!a.TiendaReceptoraId.HasValue || x.TipoDestino == "Comercio" && x.TiendaDestinoId == a.TiendaReceptoraId))
            .OrderByDescending(x => x.Abierta).ThenByDescending(x => x.Id).Take(500).ToListAsync();
        var query = db.ParadasEntrega.AsNoTracking().Include(x => x.OrdenEntrega)
            .Where(x => x.ClienteAppId == a.TenantId && (!ordenId.HasValue || x.OrdenEntregaId == ordenId));
        query = a.TiendaReceptoraId.HasValue ? query.Where(p => false)
            : a.EsPiloto ? query.Where(p => p.OrdenEntrega.GestionPorParadas && p.PilotoAsignadoId == a.PilotoId && !p.EnReasignacion)
            : query.Where(p => ordenId.HasValue || p.EnReasignacion);
        var stops = await query.OrderBy(p => p.OrdenEntregaId).ThenBy(p => p.Posicion).Take(1000).ToListAsync();
        var contactIds = requests.SelectMany(x => new[] { x.PilotoOrigenId, x.PilotoDestinoId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var contacts = await db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == a.TenantId && contactIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Nombre, p.Telefono });
        return new(stops.Select(p => new ParadaReasignacionDto(p.OrdenEntregaId, p.Id,
            p.OrdenEntrega.CodigoA ?? p.OrdenEntrega.NumeroOrdenA.ToString(), p.Estado.ToString(), p.Posicion,
            !a.EsPiloto || p.RecogidaParadaAt.HasValue ? p.Nombre : "Destino pendiente de recepción",
            p.OrdenEntrega.GestionPorParadas ? p.PilotoAsignadoId : p.OrdenEntrega.PilotoId,
            p.PilotoCustodioId, p.EnReasignacion, p.RecogidaParadaAt, p.RevisionAsignacion)).ToList(),
            requests.Select(x => RetiroDto(x, a) with {
                PilotoOrigenNombre = x.PilotoOrigenId.HasValue ? contacts.GetValueOrDefault(x.PilotoOrigenId.Value)?.Nombre : null,
                PilotoOrigenTelefono = x.PilotoOrigenId.HasValue ? contacts.GetValueOrDefault(x.PilotoOrigenId.Value)?.Telefono : null,
                PilotoDestinoNombre = x.PilotoDestinoId.HasValue ? contacts.GetValueOrDefault(x.PilotoDestinoId.Value)?.Nombre : null,
                PilotoDestinoTelefono = x.PilotoDestinoId.HasValue ? contacts.GetValueOrDefault(x.PilotoDestinoId.Value)?.Telefono : null
            }).ToList());
    }

    public async Task<object> SolicitarRetiroParadasAsync(OperationsActor a, SolicitarRetiroParadasRequest r)
        => await MutateAsync(a, "SolicitarRetiroParadas", r.OperacionId, r, async () =>
        {
            if (a.TiendaReceptoraId.HasValue) throw new OperationsException(403, "El comercio receptor sólo confirma sus recepciones.");
            var order = await LoadReassignmentOrderAsync(a, r.OrdenId);
            var stops = SelectReassignmentStops(order, r.Paradas);
            var reason = Required(r.Motivo, 1000, "Motivo");
            foreach (var p in stops)
                if (a.EsPiloto && (order.GestionPorParadas ? p.PilotoAsignadoId : order.PilotoId) != a.PilotoId)
                    throw new OperationsException(404, "Parada no disponible para este piloto.");
            if (r.IncidenciaId.HasValue && !await db.IncidenciasOperativas.AnyAsync(i => i.Id == r.IncidenciaId
                && i.ClienteAppId == a.TenantId && i.OrdenEntregaId == order.Id
                && (!i.ParadaEntregaId.HasValue || stops.Select(p => p.Id).Contains(i.ParadaEntregaId.Value))))
                throw new OperationsException(404, "Incidencia no disponible para estas paradas.");
            await AssertNoOpenRetiroAsync(a.TenantId, stops.Select(p => p.Id).ToArray());
            await PrepareManagedOrderAsync(order);
            var items = new List<RetiroParadaOperativa>();
            foreach (var p in stops)
            {
                var item = NewRetiro(a, order, p, reason);
                item.IncidenciaId = r.IncidenciaId;
                if (!a.EsPiloto) { item.Estado = "PorReasignar"; item.DecididaPor = a.UserId; RetireAssignment(p); }
                db.RetirosParadas.Add(item); items.Add(item);
            }
            if (!a.EsPiloto) await ReleaseManagedRouteStopsAsync(a.TenantId, order.Id, stops.Select(p => p.Id).ToArray());
            ReconcileManagedOrder(order, Now);
            HistoryReassignment(a, order, a.EsPiloto ? "Solicitud de retiro de paradas registrada." : "Gestor retiró paradas para reasignar o cancelar.");
            await db.SaveChangesAsync();
            return new { gestiones = items.Select(x => RetiroDto(x, a)).ToList() };
        });

    public async Task<object> DecidirRetiroParadaAsync(OperationsActor a, int id, DecidirRetiroParadaRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "DecidirRetiroParada", r.OperacionId, new { id, r }, async () =>
        {
            var item = await LoadRetiroAsync(a, id); CheckVersion(item, r.Version);
            if (item.Estado != "Solicitada" || !item.Abierta) throw new OperationsException(409, "La solicitud ya fue decidida.");
            item.NotaDecision = Required(r.Nota, 1000, "Nota de decisión"); item.DecididaPor = a.UserId;
            if (r.Accion == "Rechazar") { item.Estado = "Rechazada"; item.Abierta = false; }
            else if (r.Accion == "Aprobar")
            {
                var p = item.ParadaEntrega;
                if (EstadosEntrega.EsTerminal(p.Estado) || p.PilotoAsignadoId != item.PilotoOrigenId)
                    throw new OperationsException(409, "La parada cambió de responsable o ya terminó.");
                RetireAssignment(p); item.Estado = "PorReasignar";
                await ReleaseManagedRouteStopsAsync(a.TenantId, item.OrdenEntregaId, [p.Id]);
                ReconcileManagedOrder(item.OrdenEntrega, Now);
            }
            else throw new OperationsException(400, "Selecciona Aprobar o Rechazar.");
            Touch(item); HistoryReassignment(a, item.OrdenEntrega, $"{r.Accion} retiro de parada {item.ParadaEntregaId}. {item.NotaDecision}");
            return RetiroDto(item, a);
        });
    }

    public async Task<object> AsignarParadasAsync(OperationsActor a, AsignarParadasRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "AsignarParadas", r.OperacionId, r, async () =>
        {
            var order = await LoadReassignmentOrderAsync(a, r.OrdenId);
            var stops = SelectReassignmentStops(order, r.Paradas);
            var reason = Required(r.Motivo, 1000, "Motivo de asignación");
            var target = await db.Pilotos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == r.PilotoId && p.ClienteAppId == a.TenantId && p.Activo)
                ?? throw new OperationsException(404, "El piloto receptor no está activo en esta empresa.");
            if (stops.Any(p => (order.GestionPorParadas ? p.PilotoAsignadoId : order.PilotoId) == target.Id && !p.EnReasignacion))
                throw new OperationsException(409, "Una parada ya está asignada al piloto seleccionado.");
            await PrepareManagedOrderAsync(order);
            var items = new List<RetiroParadaOperativa>();
            foreach (var p in stops)
            {
                var open = await db.RetirosParadas.SingleOrDefaultAsync(x => x.ClienteAppId == a.TenantId && x.ParadaEntregaId == p.Id && x.Abierta);
                if (open is not null && open.Estado != "PorReasignar") throw new OperationsException(409, "Decide la solicitud o termina la recepción pendiente antes de reasignar.");
                var item = open ?? NewRetiro(a, order, p, reason);
                if (open is null) db.RetirosParadas.Add(item);
                item.PilotoDestinoId = target.Id; item.TipoDestino = "Piloto";
                item.DestinoNombre = target.Nombre; item.DestinoDireccion = "Coordina la recepción con el custodio y el gestor.";
                item.DecididaPor = a.UserId; item.NotaDecision = reason;
                var receiptRequired = p.PilotoCustodioId.HasValue || p.RecogidaActualDireccion is not null;
                p.PilotoAsignadoId = target.Id; p.RecogidaParadaAt = null;
                p.RevisionAsignacion = Guid.NewGuid().ToString("N");
                p.EnReasignacion = receiptRequired;
                item.Estado = receiptRequired ? "PendienteRecepcion" : "Reasignada";
                item.Abierta = receiptRequired; Touch(item); items.Add(item);
            }
            await ReleaseManagedRouteStopsAsync(a.TenantId, order.Id, stops.Select(p => p.Id).ToArray());
            foreach (var p in stops)
                await new OperationsWorkGuard(db, clock).ValidateParadaAssignmentAsync(a.TenantId, order.Id, p.Id, target.Id);
            ReconcileManagedOrder(order, Now);
            HistoryReassignment(a, order, $"Paradas {string.Join(",", stops.Select(p => p.Id))} asignadas al piloto {target.Id}; recepción física independiente.");
            await db.SaveChangesAsync();
            return new { gestiones = items.Select(x => RetiroDto(x, a)).ToList() };
        });
    }

    public async Task<object> ProgramarRetornoParadaAsync(OperationsActor a, int id, ProgramarRetornoParadaRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "ProgramarRetornoParada", r.OperacionId, new { id, r }, async () =>
        {
            var item = await LoadRetiroAsync(a, id); CheckVersion(item, r.Version);
            if (!item.Abierta || item.Estado != "PorReasignar" || item.ParadaEntrega.PilotoCustodioId is null)
                throw new OperationsException(409, "No existe un paquete en custodia pendiente de devolver.");
            if (r.TiendaId.HasValue)
            {
                var shop = await db.Tiendas.AsNoTracking().SingleOrDefaultAsync(t => t.Id == r.TiendaId && t.ClienteAppId == a.TenantId && t.Activo)
                    ?? throw new OperationsException(404, "Comercio receptor no disponible en esta empresa.");
                item.TipoDestino = "Comercio"; item.TiendaDestinoId = shop.Id;
                item.DestinoNombre = shop.Nombre; item.DestinoDireccion = shop.Direccion;
                item.DestinoLatitud = shop.Latitud; item.DestinoLongitud = shop.Longitud;
            }
            else
            {
                item.TipoDestino = "Centro"; item.TiendaDestinoId = null;
                item.DestinoNombre = Required(r.NombreCentro, 120, "Nombre del centro receptor");
                item.DestinoDireccion = Required(r.DireccionCentro, 250, "Dirección del centro receptor");
                if (r.Latitud.HasValue != r.Longitud.HasValue || r.Latitud is < -90 or > 90 || r.Longitud is < -180 or > 180
                    || r.Latitud.HasValue && (!double.IsFinite(r.Latitud.Value) || !double.IsFinite(r.Longitud!.Value)))
                    throw new OperationsException(400, "Coordenadas del centro no válidas.");
                item.DestinoLatitud = r.Latitud; item.DestinoLongitud = r.Longitud;
            }
            item.Estado = "PendienteRecepcion"; item.PilotoDestinoId = null; item.DecididaPor = a.UserId; Touch(item);
            HistoryReassignment(a, item.OrdenEntrega, $"Retorno de parada {item.ParadaEntregaId} a {item.DestinoNombre} autorizado; el piloto conserva la custodia hasta recepción.");
            return RetiroDto(item, a);
        });
    }

    public async Task<object> ConfirmarRecepcionParadaAsync(OperationsActor a, int id, ConfirmarRecepcionParadaRequest r)
        => await MutateAsync(a, "ConfirmarRecepcionParada", r.OperacionId, new { id, r }, async () =>
        {
            var item = await LoadRetiroAsync(a, id); CheckVersion(item, r.Version);
            if (!item.Abierta || item.Estado != "PendienteRecepcion") throw new OperationsException(409, "La recepción ya cambió o no está pendiente.");
            if (item.TipoDestino == "Piloto" && (!a.EsPiloto || item.PilotoDestinoId != a.PilotoId))
                throw new OperationsException(403, "La recepción debe confirmarla el piloto receptor desde su app.");
            if (item.TipoDestino != "Piloto" && a.EsPiloto) throw new OperationsException(403, "La recepción en centro o comercio requiere su responsable autorizado.");
            if (a.TiendaReceptoraId.HasValue && (item.TipoDestino != "Comercio" || item.TiendaDestinoId != a.TiendaReceptoraId))
                throw new OperationsException(404, "Recepción no disponible para este comercio.");
            var observation = Required(r.Observaciones, 1000, "Confirmación de recepción");
            var p = item.ParadaEntrega;
            if (item.TipoDestino == "Piloto")
            {
                if (EstadosEntrega.EsTerminal(p.Estado) || p.PilotoAsignadoId != item.PilotoDestinoId || !p.EnReasignacion)
                    throw new OperationsException(409, "La asignación cambió; no se puede recibir esta entrega.");
                if (!await db.Pilotos.AnyAsync(x => x.Id == item.PilotoDestinoId && x.ClienteAppId == a.TenantId && x.Activo))
                    throw new OperationsException(409, "El piloto receptor ya no está activo.");
                p.PilotoCustodioId = item.PilotoDestinoId; p.RecogidaParadaAt = Now; p.EnReasignacion = false;
            }
            else
            {
                p.PilotoCustodioId = null; p.PilotoAsignadoId = null; p.RecogidaParadaAt = null;
                p.EnReasignacion = !EstadosEntrega.EsTerminal(p.Estado);
                p.RecogidaActualNombre = item.DestinoNombre; p.RecogidaActualDireccion = item.DestinoDireccion;
                p.RecogidaActualLatitud = item.DestinoLatitud; p.RecogidaActualLongitud = item.DestinoLongitud;
            }
            p.RevisionAsignacion = Guid.NewGuid().ToString("N");
            item.Estado = "Recibida"; item.Abierta = false; item.RecibidaPor = a.UserId; item.RecibidaAt = Now; Touch(item);
            ReconcileManagedOrder(item.OrdenEntrega, Now);
            HistoryReassignment(a, item.OrdenEntrega, $"Recepción física de parada {p.Id}: {observation}. No equivale a entrega C.");
            return RetiroDto(item, a);
        });

    public async Task<object> CancelarParadasAsync(OperationsActor a, CancelarParadasRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "CancelarParadas", r.OperacionId, r, async () =>
        {
            var order = await LoadReassignmentOrderAsync(a, r.OrdenId);
            var stops = SelectReassignmentStops(order, r.Paradas);
            var reason = Required(r.Motivo, 1000, "Motivo de cancelación");
            await PrepareManagedOrderAsync(order);
            foreach (var p in stops)
            {
                var open = await db.RetirosParadas.SingleOrDefaultAsync(x => x.ClienteAppId == a.TenantId && x.ParadaEntregaId == p.Id && x.Abierta);
                if (open is not null && open.Estado == "Solicitada") throw new OperationsException(409, "Primero decide la solicitud de retiro pendiente.");
                var item = open ?? NewRetiro(a, order, p, reason);
                if (open is null) db.RetirosParadas.Add(item);
                p.Estado = EstadoParadaEntrega.Cancelada; p.PilotoAsignadoId = null; p.EnReasignacion = false;
                p.RevisionAsignacion = Guid.NewGuid().ToString("N");
                item.DecididaPor = a.UserId; item.NotaDecision = reason;
                if (p.PilotoCustodioId.HasValue)
                {
                    // Cancelar la entrega no borra la obligación de devolver el paquete.
                    if (item.TipoDestino == "Piloto") { item.PilotoDestinoId = null; item.TipoDestino = null; item.Estado = "PorReasignar"; }
                    else if (item.Estado != "PendienteRecepcion") item.Estado = "PorReasignar";
                    item.Abierta = true;
                }
                else { item.Estado = "Cancelada"; item.Abierta = false; }
                Touch(item);
                foreach (var code in await db.CodigosEntrega.Where(c => c.ClienteAppId == a.TenantId && c.OrdenEntregaId == order.Id && c.ParadaEntregaId == p.Id).ToListAsync()) code.Reservado = false;
            }
            await ReleaseManagedRouteStopsAsync(a.TenantId, order.Id, stops.Select(p => p.Id).ToArray());
            ReconcileManagedOrder(order, Now);
            if (EstadosEntrega.EsTerminal(order.Estado))
                foreach (var code in await db.CodigosEntrega.Where(c => c.ClienteAppId == a.TenantId && c.OrdenEntregaId == order.Id).ToListAsync()) code.Reservado = false;
            HistoryReassignment(a, order, $"Canceladas paradas {string.Join(",", stops.Select(p => p.Id))}: {reason}");
            return new { ordenId = order.Id, estadoOrden = order.Estado.ToString(), paradas = stops.Select(p => p.Id).ToList() };
        });
    }

    private async Task<OrdenEntrega> LoadReassignmentOrderAsync(OperationsActor a, int id)
        => await db.OrdenesEntrega.Include(o => o.Paradas).SingleOrDefaultAsync(o => o.Id == id && o.ClienteAppId == a.TenantId
            && (!a.EsPiloto || !o.GestionPorParadas && o.PilotoId == a.PilotoId
                || o.GestionPorParadas && o.Paradas.Any(p => p.PilotoAsignadoId == a.PilotoId && !p.EnReasignacion)))
            ?? throw new OperationsException(404, "Pedido no disponible en esta empresa.");
    private static List<ParadaEntrega> SelectReassignmentStops(OrdenEntrega order, List<RevisionParadaRequest>? selected)
    {
        if (selected is null || selected.Count == 0 || selected.Count > 100 || selected.Select(x => x.ParadaId).Distinct().Count() != selected.Count)
            throw new OperationsException(400, "Selecciona entre una y cien paradas sin duplicados.");
        var items = new List<ParadaEntrega>();
        foreach (var input in selected)
        {
            var p = order.Paradas.SingleOrDefault(x => x.Id == input.ParadaId)
                ?? throw new OperationsException(404, "Una parada no pertenece al pedido.");
            if (EstadosEntrega.EsTerminal(p.Estado)) throw new OperationsException(409, "Las paradas finalizadas conservan su historial y no pueden reasignarse.");
            if (input.RevisionAsignacion != p.RevisionAsignacion) throw new OperationsException(409, "La asignación cambió; actualiza las paradas.");
            items.Add(p);
        }
        return items;
    }
    private async Task PrepareManagedOrderAsync(OrdenEntrega order)
    {
        if (order.GestionPorParadas) return;
        if (await db.DevolucionesOperativas.AnyAsync(d => d.ClienteAppId == order.ClienteAppId && d.OrdenEntregaId == order.Id && d.Abierta))
            throw new OperationsException(409, "Termina o rechaza primero la devolución definitiva pendiente.");
        foreach (var p in order.Paradas)
        {
            p.PilotoAsignadoId = order.PilotoId;
            p.PilotoEntregaId = p.Estado == EstadoParadaEntrega.Entregada ? order.PilotoId : null;
            p.PilotoCustodioId = !EstadosEntrega.EsTerminal(p.Estado) && order.RecolectadaAt.HasValue ? order.PilotoId : null;
            p.RecogidaParadaAt = order.RecolectadaAt;
            p.RevisionAsignacion = Guid.NewGuid().ToString("N");
            p.EnReasignacion = !EstadosEntrega.EsTerminal(p.Estado) && !order.PilotoId.HasValue;
        }
        order.GestionPorParadas = true; order.EsAuto = false;
        await ConvertManagedRouteMembershipsAsync(order);
    }
    private async Task AssertNoOpenRetiroAsync(int tenant, int[] ids)
    {
        if (await db.RetirosParadas.AnyAsync(x => x.ClienteAppId == tenant && ids.Contains(x.ParadaEntregaId) && x.Abierta))
            throw new OperationsException(409, "Ya existe una gestión pendiente para alguna parada.");
    }
    private RetiroParadaOperativa NewRetiro(OperationsActor a, OrdenEntrega order, ParadaEntrega p, string reason)
        => new() { ClienteAppId = a.TenantId, OrdenEntregaId = order.Id, OrdenEntrega = order,
            ParadaEntregaId = p.Id, ParadaEntrega = p, PilotoOrigenId = p.PilotoCustodioId ?? p.PilotoAsignadoId,
            Motivo = reason, SolicitadaPor = a.UserId, CreatedAt = Now, UpdatedAt = Now };
    private static void RetireAssignment(ParadaEntrega p)
    { p.PilotoAsignadoId = null; p.EnReasignacion = true; p.RevisionAsignacion = Guid.NewGuid().ToString("N"); }
    private async Task<RetiroParadaOperativa> LoadRetiroAsync(OperationsActor a, int id)
        => await db.RetirosParadas.Include(x => x.OrdenEntrega).ThenInclude(o => o.Paradas).Include(x => x.ParadaEntrega)
            .SingleOrDefaultAsync(x => x.Id == id && x.ClienteAppId == a.TenantId
                && (!a.EsPiloto || x.PilotoOrigenId == a.PilotoId || x.PilotoDestinoId == a.PilotoId)
                && (!a.TiendaReceptoraId.HasValue || x.TipoDestino == "Comercio" && x.TiendaDestinoId == a.TiendaReceptoraId))
            ?? throw new OperationsException(404, "Gestión no disponible para este receptor.");
    private static RetiroParadaDto RetiroDto(RetiroParadaOperativa x, OperationsActor a)
        => new(x.Id, x.OrdenEntregaId, x.ParadaEntregaId, x.OrdenEntrega.CodigoA ?? x.OrdenEntrega.NumeroOrdenA.ToString(),
            x.Estado, x.Motivo, x.NotaDecision, x.PilotoOrigenId, x.PilotoDestinoId, x.ParadaEntrega.PilotoCustodioId,
            x.TipoDestino, x.TiendaDestinoId, x.DestinoNombre, x.DestinoDireccion, x.DestinoLatitud, x.DestinoLongitud,
            x.CreatedAt, x.RecibidaAt, x.Abierta && x.Estado == "PendienteRecepcion"
                && (x.TipoDestino == "Piloto" ? a.EsPiloto && x.PilotoDestinoId == a.PilotoId
                    : !a.EsPiloto && (!a.TiendaReceptoraId.HasValue || x.TipoDestino == "Comercio" && x.TiendaDestinoId == a.TiendaReceptoraId)),
            x.ParadaEntrega.RevisionAsignacion, x.Version, x.Abierta);
    private void HistoryReassignment(OperationsActor a, OrdenEntrega order, string text)
    {
        db.HistorialOrdenes.Add(new() { OrdenEntregaId = order.Id, ActorIdentityUserId = a.UserId,
            ActorRol = a.EsPiloto ? "Piloto" : a.TiendaReceptoraId.HasValue ? "Cliente" : "Gestor", TipoEvento = TipoEventoOrden.Comentario,
            Descripcion = text.Length > 500 ? text[..500] : text, CreatedAt = Now });
        Audit(a, "ReasignacionParada", order.Id, text);
    }
    internal static void ReconcileManagedOrder(OrdenEntrega order, DateTime? at = null)
    {
        if (!order.GestionPorParadas) return;
        var active = order.Paradas.Where(p => !EstadosEntrega.EsTerminal(p.Estado)).ToList();
        order.EsAuto = false;
        var pilots = active.Select(p => p.PilotoAsignadoId).Distinct().ToList();
        order.PilotoId = pilots.Count == 1 ? pilots[0] : null;
        if (active.Count > 0)
        {
            order.Estado = order.RecolectadaAt.HasValue || order.Paradas.Any(p => p.RecogidaParadaAt.HasValue || p.PilotoCustodioId.HasValue)
                ? EstadoOrden.Recolectada : active.Any(p => p.PilotoAsignadoId.HasValue) ? EstadoOrden.Asignada : EstadoOrden.Creada;
            return;
        }
        order.FinalizadaAt ??= at ?? DateTime.UtcNow;
        order.Estado = order.Paradas.All(p => p.Estado == EstadoParadaEntrega.Entregada) ? EstadoOrden.Entregada
            : order.Paradas.All(p => p.Estado == EstadoParadaEntrega.Cancelada) ? EstadoOrden.Cancelada
            : order.Paradas.All(p => p.Estado == EstadoParadaEntrega.Devuelta) ? EstadoOrden.Devuelta : EstadoOrden.FinalizadaConDevoluciones;
        if (order.Estado == EstadoOrden.Entregada) order.EntregadaAt ??= at ?? DateTime.UtcNow;
    }
}
