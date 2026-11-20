using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    private async Task<IncidenciaOperativa?> ReassignmentIncidentAsync(OperationsActor a, int orderId,
        IReadOnlyCollection<ParadaEntrega> stops, int? incidentId, bool includeSiblings = false)
    {
        if (!incidentId.HasValue) return null;
        var incident = await db.IncidenciasOperativas.SingleOrDefaultAsync(i => i.Id == incidentId
            && i.ClienteAppId == a.TenantId && i.OrdenEntregaId == orderId)
            ?? throw new OperationsException(404, "Incidencia no disponible para este pedido.");
        if (incident.ParadaEntregaId.HasValue && (!stops.Any(p => p.Id == incident.ParadaEntregaId)
            || !includeSiblings && stops.Any(p => p.Id != incident.ParadaEntregaId)))
            throw new OperationsException(404, "La gestión debe limitarse a la parada de la incidencia.");
        return incident;
    }

    private async Task<IncidenciaOperativa> CreateRetirementIncidentAsync(OperationsActor a, int orderId,
        IReadOnlyCollection<ParadaEntrega> stops, string reason)
    {
        const string category = "Retiro de paquete";
        var rule = await db.ReglasIncidencia.SingleOrDefaultAsync(x => x.ClienteAppId == a.TenantId
            && x.Categoria == category && x.Activa);
        if (rule?.Efecto == EfectoIncidencia.PausarParada && stops.Count > 1)
            throw new OperationsException(409, "La política pausa únicamente la parada afectada. Solicita el retiro por separado para cada parada para conservar ese alcance.");
        var stopIds = stops.Select(p => p.Id).ToArray();
        // La pausa de una ruta debe seguir ligada al recorrido donde se reportó,
        // incluso después de retirar sus membresías durante la aprobación.
        var routeIds = await db.RutaOrdenesOperativas.AsNoTracking()
            .Where(m => m.ClienteAppId == a.TenantId && m.OrdenEntregaId == orderId && m.Vigente
                && m.Ruta.Estado == EstadoRutaOperativa.Activa
                && (!a.EsPiloto || m.Ruta.PilotoId == a.PilotoId)
                && (!m.ParadaEntregaId.HasValue || stopIds.Contains(m.ParadaEntregaId.Value)))
            .Select(m => m.RutaId).Distinct().ToListAsync();
        if (rule?.Efecto == EfectoIncidencia.PausarRuta && routeIds.Count > 1)
            throw new OperationsException(409, "La política pausa una ruta completa. Solicita el retiro por separado para cada ruta afectada.");
        var incident = new IncidenciaOperativa
        {
            ClienteAppId = a.TenantId, OrdenEntregaId = orderId,
            ParadaEntregaId = stops.Count == 1 ? stops.Single().Id : null,
            RutaId = routeIds.Count == 1 ? routeIds[0] : null,
            Categoria = category, Descripcion = reason, ReportadaPor = a.UserId,
            Prioridad = rule?.Prioridad ?? 2, Efecto = rule?.Efecto ?? EfectoIncidencia.Continuar,
            VenceAt = rule?.MinutosResolucion is int minutes ? Now.AddMinutes(minutes) : null,
            ReglaVersion = rule?.Version, CreatedAt = Now, UpdatedAt = Now
        };
        incident.Actividad.Add(new() { ClienteAppId = a.TenantId, ActorId = a.UserId, Accion = "Registro",
            Texto = "Solicitud de retiro de paquete registrada. Requiere aprobación del gestor; no confirma entrega ni recepción.", CreatedAt = Now });
        db.IncidenciasOperativas.Add(incident);
        await db.SaveChangesAsync();
        Audit(a, "Incidencia", incident.Id, "Solicitud de retiro vinculada al pedido y sus paradas.");
        return incident;
    }

    private static void LinkRetirementIncident(RetiroParadaOperativa item, int? incidentId)
    {
        if (!incidentId.HasValue) return;
        if (item.IncidenciaId.HasValue && item.IncidenciaId != incidentId)
            throw new OperationsException(409, "La gestión ya está vinculada a otra incidencia; revisa su historial.");
        item.IncidenciaId = incidentId;
    }

    private async Task RetirementIncidentActivityAsync(OperationsActor a, RetiroParadaOperativa item,
        string action, string text)
    {
        if (!item.IncidenciaId.HasValue) return;
        var incident = await db.IncidenciasOperativas.SingleOrDefaultAsync(i => i.Id == item.IncidenciaId
            && i.ClienteAppId == a.TenantId && i.OrdenEntregaId == item.OrdenEntregaId)
            ?? throw new OperationsException(409, "La incidencia vinculada ya no está disponible.");
        incident.Actividad.Add(new() { ClienteAppId = a.TenantId, ActorId = a.UserId, Accion = action,
            Texto = $"Parada {item.ParadaEntregaId}: {text}", CreatedAt = Now });
        if (!a.EsPiloto && !a.TiendaReceptoraId.HasValue)
        {
            if (incident.Estado == EstadoIncidencia.Nueva) incident.Estado = EstadoIncidencia.EnAtencion;
            incident.ResponsableId ??= a.UserId;
        }
        // La actividad de paquetes no levanta pausas ni resuelve incidencias.
        Touch(incident);
    }

    public async Task<object> TrasladarDevolucionesAReasignacionAsync(OperationsActor a, TrasladarDevolucionesRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "TrasladarDevoluciones", r.OperacionId, r, async () =>
        {
            var order = await LoadReassignmentOrderAsync(a, r.OrdenId);
            if (order.GestionPorParadas || order.Estado != EstadoOrden.Recolectada
                || !order.RecolectadaAt.HasValue || !order.PilotoId.HasValue)
                throw new OperationsException(409, "El pedido ya cambió de modalidad o de custodia; actualiza la gestión.");
            if (r.Devoluciones is null || r.Devoluciones.Count is < 1 or > 100
                || r.Devoluciones.Select(x => x.Id).Distinct().Count() != r.Devoluciones.Count)
                throw new OperationsException(400, "Confirma todas las devoluciones pendientes del pedido, sin duplicados.");
            var reason = Required(r.Motivo, 1000, "Motivo del traslado");
            var returns = await db.DevolucionesOperativas.Include(x => x.ParadaEntrega)
                .Where(x => x.ClienteAppId == a.TenantId && x.OrdenEntregaId == order.Id && x.Abierta)
                .OrderBy(x => x.Id).ToListAsync();
            if (returns.Count == 0 || !returns.Select(x => x.Id).Order().SequenceEqual(r.Devoluciones.Select(x => x.Id).Order()))
                throw new OperationsException(409, "Las devoluciones pendientes cambiaron; revisa y confirma el conjunto completo.");
            foreach (var item in returns)
            {
                CheckVersion(item, r.Devoluciones.Single(x => x.Id == item.Id).Version);
                if (item.PilotoId != order.PilotoId || item.Estado is not (EstadoDevolucionOperativa.Solicitada
                    or EstadoDevolucionOperativa.Autorizada or EstadoDevolucionOperativa.EnRetorno)
                    || item.ParadaEntrega.Estado is not (EstadoParadaEntrega.Pendiente
                        or EstadoParadaEntrega.DevolucionAutorizada or EstadoParadaEntrega.EnRetorno))
                    throw new OperationsException(409, "Una devolución cambió de responsable o estado; no puede trasladarse con esta revisión.");
            }
            var stops = returns.Select(x => x.ParadaEntrega).ToList();
            var incident = await ReassignmentIncidentAsync(a, order.Id, stops, r.IncidenciaId, includeSiblings: true);
            await AssertNoOpenRetiroAsync(a.TenantId, stops.Select(p => p.Id).ToArray());
            foreach (var item in returns)
            {
                item.Estado = EstadoDevolucionOperativa.TrasladadaAReasignacion; item.Abierta = false;
                item.DecididaPor = a.UserId; item.NotaDecision = reason; Touch(item);
                item.ParadaEntrega.Estado = EstadoParadaEntrega.Pendiente;
                Audit(a, "TrasladoDevolucion", item.Id, $"Trasladada a retiro/reasignación de la parada {item.ParadaEntregaId}. {reason}");
            }
            // PrepareManagedOrder consulta las abiertas en SQL. La transición
            // se guarda dentro de la misma transacción y revierte si algo falla.
            await db.SaveChangesAsync();
            await PrepareManagedOrderAsync(order);
            var retirements = new List<RetiroParadaOperativa>();
            foreach (var p in stops)
            {
                var item = NewRetiro(a, order, p, reason);
                item.Estado = "PorReasignar"; item.DecididaPor = a.UserId;
                item.IncidenciaId = incident is not null && (!incident.ParadaEntregaId.HasValue || incident.ParadaEntregaId == p.Id) ? incident.Id : null;
                RetireAssignment(p); db.RetirosParadas.Add(item); retirements.Add(item);
                await RetirementIncidentActivityAsync(a, item, "DevolucionTrasladada",
                    "Devolución anterior trasladada a retiro y reasignación; custodia conservada hasta recepción.");
            }
            await ReleaseManagedRouteStopsAsync(a.TenantId, order.Id, stops.Select(p => p.Id).ToArray());
            ReconcileManagedOrder(order, Now);
            HistoryReassignment(a, order, $"Devoluciones {string.Join(",", returns.Select(x => x.Id))} trasladadas a retiro y reasignación. {reason}");
            await db.SaveChangesAsync();
            return new { ordenId = order.Id, gestiones = retirements.Select(x => RetiroDto(x, a)).ToList() };
        });
    }
}
