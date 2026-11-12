using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    public async Task<List<DevolucionOperativaDto>> DevolucionesAsync(OperationsActor a, int? ordenId = null)
    {
        await EnsureActorAsync(a);
        var cases = await db.DevolucionesOperativas.AsNoTracking().Include(x => x.OrdenEntrega)
            .Include(x => x.ParadaEntrega).Where(x => x.ClienteAppId == a.TenantId
                && (!a.EsPiloto || x.PilotoId == a.PilotoId) && (!ordenId.HasValue || x.OrdenEntregaId == ordenId))
            .OrderByDescending(x => x.Id).Take(1000).AsSplitQuery().ToListAsync();
        return cases.Select(DevolucionDto).ToList();
    }

    public async Task<object> SolicitarDevolucionAsync(OperationsActor a, SolicitarDevolucionRequest r)
    {
        await EnsureActorAsync(a);
        if (!a.EsPiloto) throw new OperationsException(403, "La solicitud corresponde al piloto custodio.");
        return await MutateAsync(a, "SolicitarDevolucion", r.OperacionId, r, async () => {
            var order = await OrderAsync(a, r.OrdenId);
            var stop = order.Paradas.SingleOrDefault(p => p.Id == r.ParadaId);
            if (order.Estado != EstadoOrden.Recolectada || stop?.Estado != EstadoParadaEntrega.Pendiente)
                throw new OperationsException(400, "Solo puede gestionar una entrega pendiente después de recoger.");
            if (await db.DevolucionesOperativas.AnyAsync(x => x.ClienteAppId == a.TenantId
                && x.ParadaEntregaId == stop.Id && x.Abierta))
                throw new OperationsException(409, "La parada ya tiene una solicitud abierta. Recupera esa gestión.");
            var item = new DevolucionOperativa { ClienteAppId = a.TenantId, OrdenEntregaId = order.Id,
                ParadaEntregaId = stop.Id, PilotoId = a.PilotoId!.Value,
                Tipo = EnumValue<TipoGestionFalla>(r.Tipo), Motivo = Required(r.Motivo, 1000, "Motivo"),
                SolicitadaPor = a.UserId, OrdenEntrega = order, ParadaEntrega = stop,
                CreatedAt = Now, UpdatedAt = Now };
            // Solicitar no aplica una pausa implícita; esa decisión pertenece a las políticas de incidencias.
            db.DevolucionesOperativas.Add(item); await db.SaveChangesAsync();
            Audit(a, "SolicitudFalla", item.Id, $"{item.Tipo} solicitado para orden {order.Id}, parada {stop.Id}.");
            return DevolucionDto(item);
        });
    }

    public async Task<object> DecidirDevolucionAsync(OperationsActor a, int id, DecidirDevolucionRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "DecidirDevolucion", r.OperacionId, new { id, r }, async () => {
            var item = await LoadDevolucionAsync(a, id); CheckVersion(item, r.Version);
            if (item.Estado != EstadoDevolucionOperativa.Solicitada)
                throw new OperationsException(400, "La solicitud ya fue decidida.");
            item.NotaDecision = Required(r.Nota, 1000, "Nota de decisión");
            if (r.Accion == "Rechazar") { item.Estado = EstadoDevolucionOperativa.Rechazada; item.Abierta = false; }
            else if (r.Accion == "Autorizar")
            {
                if (item.OrdenEntrega.Estado != EstadoOrden.Recolectada || item.ParadaEntrega.Estado != EstadoParadaEntrega.Pendiente)
                    throw new OperationsException(409, "La entrega cambió; no puede autorizarse con esta revisión.");
                if (item.OrdenEntrega.PilotoId != item.PilotoId)
                    throw new OperationsException(409, "La custodia cambió. Revisa la asignación.");
                item.AutorizadaAt = Now;
                if (item.Tipo == TipoGestionFalla.Reintento)
                {
                    item.Estado = EstadoDevolucionOperativa.ReintentoAutorizado; item.Abierta = false;
                    // C no cambia ni se consume; una pausa CRM no queda levantada por este comando.
                }
                else
                {
                    item.Estado = EstadoDevolucionOperativa.Autorizada;
                    item.ParadaEntrega.Estado = EstadoParadaEntrega.DevolucionAutorizada;
                    var member = await db.RutaOrdenesOperativas.Include(x => x.Ruta).SingleOrDefaultAsync(x =>
                        x.ClienteAppId == a.TenantId && x.OrdenEntregaId == item.OrdenEntregaId && x.Vigente);
                    if (member is not null)
                    {
                        var last = await db.PuntosRutaOperativos.Where(p => p.RutaId == member.RutaId)
                            .Select(p => (int?)p.Posicion).MaxAsync() ?? 0;
                        db.PuntosRutaOperativos.Add(new() { ClienteAppId = a.TenantId, RutaId = member.RutaId,
                            RutaOrdenId = member.Id, OrdenEntregaId = item.OrdenEntregaId,
                            ParadaEntregaId = item.ParadaEntregaId, Tipo = TipoPuntoRuta.Devolucion, Posicion = last + 1 });
                        Touch(member.Ruta);
                    }
                }
            }
            else throw new OperationsException(400, "Selecciona Autorizar o Rechazar.");
            item.DecididaPor = a.UserId; Touch(item);
            Audit(a, "DecisionFalla", item.Id, $"{r.Accion} {item.Tipo}, parada {item.ParadaEntregaId}. {item.NotaDecision}");
            return DevolucionDto(item);
        });
    }

    public async Task<object> IniciarRetornoAsync(OperationsActor a, int id, OperacionVersionRequest r)
    {
        await EnsureActorAsync(a);
        if (!a.EsPiloto) throw new OperationsException(403, "El piloto custodio inicia el retorno.");
        return await MutateAsync(a, "IniciarRetorno", r.OperacionId, new { id, r }, async () => {
            var item = await LoadDevolucionAsync(a, id); CheckVersion(item, r.Version);
            if (item.Tipo != TipoGestionFalla.Devolucion || item.Estado != EstadoDevolucionOperativa.Autorizada
                || item.ParadaEntrega.Estado != EstadoParadaEntrega.DevolucionAutorizada
                || item.OrdenEntrega.PilotoId != a.PilotoId)
                throw new OperationsException(400, "El retorno no está autorizado para este piloto.");
            await new OperationsWorkGuard(db, clock).ValidateRetornoAsync(a.TenantId, item.OrdenEntregaId, item.ParadaEntregaId);
            item.Estado = EstadoDevolucionOperativa.EnRetorno; item.ParadaEntrega.Estado = EstadoParadaEntrega.EnRetorno;
            item.IniciadaAt = Now; Touch(item); Audit(a, "Retorno", item.Id, "Retorno iniciado por el piloto custodio.");
            return DevolucionDto(item);
        });
    }

    public async Task<object> RecibirDevolucionAsync(OperationsActor a, int id, RecibirDevolucionRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "RecibirDevolucion", r.OperacionId, new { id, r }, async () => {
            var item = await LoadDevolucionAsync(a, id); CheckVersion(item, r.Version);
            if (item.Estado != EstadoDevolucionOperativa.EnRetorno || item.ParadaEntrega.Estado != EstadoParadaEntrega.EnRetorno)
                throw new OperationsException(400, "El retorno todavía no está pendiente de recepción.");
            if (r.TiendaId != item.OrdenEntrega.TiendaId || !await db.Tiendas.AnyAsync(t => t.Id == r.TiendaId
                && t.ClienteAppId == a.TenantId && t.Activo))
                throw new OperationsException(400, "La recepción debe registrarse en el comercio de origen autorizado.");
            item.ObservacionesRecepcion = Required(r.Observaciones, 1000, "Observaciones de recepción");
            item.RecibidaPor = a.UserId; item.TiendaRecepcionId = r.TiendaId;
            item.Estado = EstadoDevolucionOperativa.Recibida; item.Abierta = false; item.RecibidaAt = Now; Touch(item);
            item.ParadaEntrega.Estado = EstadoParadaEntrega.Devuelta;
            item.ParadaEntrega.DevueltaAt = Now;
            var code = await db.CodigosEntrega.SingleAsync(c => c.ClienteAppId == a.TenantId
                && c.OrdenEntregaId == item.OrdenEntregaId && c.ParadaEntregaId == item.ParadaEntregaId
                && c.Tipo == TipoCodigo.C_Finalizacion);
            code.Reservado = false; // La devolución nunca consume C como prueba de entrega.
            var allStops = await db.ParadasEntrega.Where(p => p.ClienteAppId == a.TenantId
                && p.OrdenEntregaId == item.OrdenEntregaId).ToListAsync();
            if (allStops.All(p => EstadosEntrega.EsTerminal(p.Estado)))
            {
                item.OrdenEntrega.Estado = allStops.All(p => p.Estado == EstadoParadaEntrega.Devuelta)
                    ? EstadoOrden.Devuelta : EstadoOrden.FinalizadaConDevoluciones;
                item.OrdenEntrega.FinalizadaAt = Now;
                foreach (var c in await db.CodigosEntrega.Where(c => c.OrdenEntregaId == item.OrdenEntregaId
                    && c.ClienteAppId == a.TenantId).ToListAsync()) c.Reservado = false;
            }
            db.HistorialOrdenes.Add(new() { OrdenEntregaId = item.OrdenEntregaId, TipoEvento = TipoEventoOrden.Comentario,
                ActorIdentityUserId = a.UserId, ActorRol = "Gestor", CreatedAt = Now,
                Descripcion = $"Devolución recibida de parada {item.ParadaEntregaId} en comercio {r.TiendaId}; no es entrega al destinatario." });
            Audit(a, "RecepcionRetorno", item.Id, $"Gestor confirmó recepción en comercio {r.TiendaId}. {item.ObservacionesRecepcion}");
            await db.SaveChangesAsync();
            await new OperationsWorkGuard(db, clock).ReconcileCompletedRoutesAsync(a.TenantId, item.OrdenEntregaId);
            return DevolucionDto(item);
        });
    }

    private async Task<DevolucionOperativa> LoadDevolucionAsync(OperationsActor a, int id)
        => await db.DevolucionesOperativas.Include(x => x.OrdenEntrega).Include(x => x.ParadaEntrega)
            .SingleOrDefaultAsync(x => x.Id == id && x.ClienteAppId == a.TenantId && (!a.EsPiloto || x.PilotoId == a.PilotoId))
            ?? throw new OperationsException(404, "Gestión no disponible.");

    public async Task<object> CancelarOrdenAsync(OperationsActor a, int id, CancelarOrdenRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "CancelarOrden", r.OperacionId, new { id, r }, async () => {
            var order = await OrderAsync(a, id);
            if (order.RecolectadaAt.HasValue || order.Estado is not (EstadoOrden.Creada or EstadoOrden.Asignada))
                throw new OperationsException(400, "Solo puede cancelarse antes de recoger; después se requiere gestión de devolución.");
            var motivo = Required(r.Motivo, 500, "Motivo de cancelación");
            order.Estado = EstadoOrden.Cancelada; order.FinalizadaAt = Now;
            foreach (var stop in order.Paradas) stop.Estado = EstadoParadaEntrega.Cancelada;
            foreach (var code in await db.CodigosEntrega.Where(c => c.ClienteAppId == a.TenantId
                && c.OrdenEntregaId == id).ToListAsync()) code.Reservado = false;
            var data = await db.DatosEnviosOperativos.SingleOrDefaultAsync(d => d.ClienteAppId == a.TenantId && d.OrdenEntregaId == id);
            if (data is null) { data = new() { ClienteAppId = a.TenantId, OrdenEntregaId = id }; db.DatosEnviosOperativos.Add(data); }
            data.CanceladaAt = Now; data.MotivoCancelacion = motivo; Touch(data);
            db.HistorialOrdenes.Add(new() { OrdenEntregaId = id, TipoEvento = TipoEventoOrden.Comentario,
                ActorIdentityUserId = a.UserId, ActorRol = "Gestor", CreatedAt = Now, Descripcion = "Orden cancelada antes de recoger. " + motivo });
            Audit(a, "CancelarOrden", id, motivo);
            await db.SaveChangesAsync();
            await new OperationsWorkGuard(db, clock).ReconcileCompletedRoutesAsync(a.TenantId, id);
            return new { id, estado = order.Estado.ToString(), finalizadaAt = order.FinalizadaAt };
        });
    }

    private static DevolucionOperativaDto DevolucionDto(DevolucionOperativa d) => new(d.Id, d.OrdenEntregaId,
        d.ParadaEntregaId, d.PilotoId, d.Tipo.ToString(), d.Estado.ToString(), d.Motivo, d.NotaDecision,
        d.CreatedAt, d.AutorizadaAt, d.IniciadaAt, d.RecibidaAt, d.OrdenEntrega.TiendaId,
        d.OrdenEntrega.RecogidaNombre, d.OrdenEntrega.RecogidaDireccion, d.OrdenEntrega.RecogidaTelefono,
        d.ParadaEntrega.Estado.ToString(), d.Version, d.ObservacionesRecepcion);
}
