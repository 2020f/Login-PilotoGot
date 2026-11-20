using System.Text.Json;
using Login.Application.Exceptions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services;

public partial class OrdenService
{
    private async Task<Piloto> PilotoActivoGestionadoAsync(string actor) => await _db.Pilotos.AsNoTracking()
        .SingleOrDefaultAsync(p => p.IdentityUserId == actor && p.Activo && p.ClienteApp.Estado == EstadoClienteApp.Activo)
        ?? throw new OperationsException(403, "Piloto no disponible.");

    private async Task<OrdenEntrega> OrdenGestionadaAsync(int ordenId, int tenant)
        => await _db.OrdenesEntrega.AsNoTracking().SingleOrDefaultAsync(o => o.Id == ordenId && o.ClienteAppId == tenant)
            ?? throw new OperationsException(404, "Orden no disponible.");

    private static void ExigirRevision(ParadaEntrega parada, string? revision)
    {
        if (string.IsNullOrWhiteSpace(revision)) throw new OperationsException(426, "La asignación requiere una revisión vigente. Actualiza la app.");
        if (!string.Equals(parada.RevisionAsignacion, revision, StringComparison.Ordinal))
            throw new RejectedOperationException("La asignación cambió. Actualiza las paradas antes de volver a confirmar.", 409);
    }

    private async Task ReproducirRecogidaLegadaGestionadaAsync(int orderId, string codigoB, string actor, string? key)
    {
        var pilot = await PilotoActivoGestionadoAsync(actor);
        using var tx = await _quotas.BeginTenantMutationAsync(pilot.ClienteAppId);
        var lockedTenant = pilot.ClienteAppId;
        pilot = await PilotoActivoGestionadoAsync(actor);
        if (pilot.ClienteAppId != lockedTenant) throw new OperationsException(403, "El acceso del piloto cambió.");
        var order = await OrdenGestionadaAsync(orderId, pilot.ClienteAppId);
        var hash = Hash(JsonSerializer.Serialize(new { ordenId = orderId, codigoB }));
        var previous = await OperacionConfirmacionAsync(order.ClienteAppId, actor, "ConfirmarRecogida", key, hash);
        if (previous is null) throw new OperationsException(426, "Utiliza la recogida por lote con la revisión de cada parada.");
        // Read-only recovery of a B that was already committed before conversion;
        // this path never changes custody or consumes B a second time.
        await tx.CommitAsync();
    }

    public async Task<RecogidaParadasResult> ConfirmarRecogidaParadasAsync(int ordenId, string codigoB,
        IReadOnlyList<RevisionParadaRequest> paradas, string actorIdentityUserId, string operacionId)
    {
        var clave = NormalizarOperacionId(operacionId) ?? throw new ArgumentException("OperacionId es obligatorio.");
        if (paradas is null || paradas.Count is < 1 or > 100 || paradas.Any(p => p.ParadaId <= 0)
            || paradas.Select(p => p.ParadaId).Distinct().Count() != paradas.Count)
            throw new ArgumentException("Selecciona entre una y cien paradas distintas.");
        var requested = paradas.OrderBy(p => p.ParadaId).ToArray();
        var hash = Hash(JsonSerializer.Serialize(new { ordenId, codigoB, paradas = requested }));
        var piloto = await PilotoActivoGestionadoAsync(actorIdentityUserId);
        using var tx = await _quotas.BeginTenantMutationAsync(piloto.ClienteAppId);
        try
        {
        var lockedTenant = piloto.ClienteAppId;
        piloto = await PilotoActivoGestionadoAsync(actorIdentityUserId);
        if (piloto.ClienteAppId != lockedTenant) throw new OperationsException(403, "El acceso del piloto cambió.");
        var orden = await OrdenGestionadaAsync(ordenId, piloto.ClienteAppId);
        var previous = await OperacionConfirmacionAsync(orden.ClienteAppId, actorIdentityUserId, "RecogerParadas", clave, hash);
        if (previous is not null)
        {
            var ack = JsonSerializer.Deserialize<RecogidaParadasResult>(previous.ResultadoJson!)!;
            await tx.CommitAsync(); return ack;
        }
        if (!orden.GestionPorParadas) throw new RejectedOperationException("Esta orden utiliza la recogida normal.");
        if (EstadosEntrega.EsTerminal(orden.Estado)) throw new RejectedOperationException("La orden ya está finalizada.", 409);
        var ids = requested.Select(p => p.ParadaId).ToArray();
        var stops = await _db.ParadasEntrega.AsNoTracking().Where(p => p.ClienteAppId == orden.ClienteAppId
            && p.OrdenEntregaId == orden.Id && ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        if (stops.Count != ids.Length) throw new RejectedOperationException("Una parada no está disponible para esta orden.", 409);
        foreach (var item in requested)
        {
            var stop = stops[item.ParadaId]; ExigirRevision(stop, item.RevisionAsignacion);
            if (stop.PilotoAsignadoId != piloto.Id || stop.EnReasignacion || stop.Estado != EstadoParadaEntrega.Pendiente
                || stop.RecogidaParadaAt.HasValue || (stop.PilotoCustodioId.HasValue && stop.PilotoCustodioId != piloto.Id))
                throw new RejectedOperationException("Una parada no está pendiente de recogida por este piloto. Revisa la recepción de custodia.", 409);
        }
        var code = await _db.CodigosEntrega.AsNoTracking().SingleOrDefaultAsync(c => c.ClienteAppId == orden.ClienteAppId
            && c.OrdenEntregaId == orden.Id && c.Tipo == TipoCodigo.B_Recoleccion && c.Codigo == codigoB && c.Reservado);
        if (code is null) throw new OrdenSolicitudRechazadaException("Código B inválido para esta orden.");
        // Respect route order even when a batch checkbox list arrives sorted by ID.
        var routePositions = await _db.PuntosRutaOperativos.AsNoTracking().Where(p => p.ClienteAppId == orden.ClienteAppId
            && p.OrdenEntregaId == orden.Id && p.ParadaEntregaId.HasValue && ids.Contains(p.ParadaEntregaId.Value)
            && p.Tipo == TipoPuntoRuta.Recogida && p.RutaOrden.Vigente && p.Ruta.PilotoId == piloto.Id)
            .Select(p => new { StopId = p.ParadaEntregaId!.Value, p.RutaId, p.Posicion }).ToListAsync();
        var positions = routePositions.ToDictionary(p => p.StopId);
        var ordered = requested.OrderBy(p => positions.GetValueOrDefault(p.ParadaId)?.RutaId ?? 0)
            .ThenBy(p => positions.GetValueOrDefault(p.ParadaId)?.Posicion ?? p.ParadaId).ToArray();
        var now = _clock.GetUtcNow().UtcDateTime;
        var guard = new OperationsWorkGuard(_db, _clock);
        foreach (var item in ordered)
        {
            await guard.ValidateAsync(orden.ClienteAppId, orden.Id, item.ParadaId, true);
            var stop = stops[item.ParadaId];
            var updated = await _db.ParadasEntrega.Where(p => p.ClienteAppId == orden.ClienteAppId
                && p.OrdenEntregaId == orden.Id && p.Id == stop.Id && p.RevisionAsignacion == item.RevisionAsignacion
                && p.PilotoAsignadoId == piloto.Id && !p.EnReasignacion && p.RecogidaParadaAt == null
                && p.Estado == EstadoParadaEntrega.Pendiente && (p.PilotoCustodioId == null || p.PilotoCustodioId == piloto.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecogidaParadaAt, now).SetProperty(p => p.PilotoCustodioId, piloto.Id));
            if (updated != 1) throw new RejectedOperationException("La recogida cambió. Actualiza la asignación.", 409);
            var local = _db.ParadasEntrega.Local.FirstOrDefault(p => p.Id == stop.Id);
            if (local is not null) await _db.Entry(local).ReloadAsync();
            await _db.PuntosRutaOperativos.Where(p => p.ClienteAppId == orden.ClienteAppId
                && p.OrdenEntregaId == orden.Id && p.ParadaEntregaId == stop.Id && p.Tipo == TipoPuntoRuta.Recogida
                && p.RutaOrden.Vigente && p.Ruta.PilotoId == piloto.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecogidaConfirmadaAt, now));
            RegistrarHistorial(orden.Id, TipoEventoOrden.Recolectada, actorIdentityUserId, "Piloto",
                $"Recogida de paradaId={stop.Id}, revisión={stop.RevisionAsignacion}, confirmada con B.");
            // Make the next pickup in the same transaction visible to the guard;
            // a later rejection still rolls back this entire batch.
            await _db.SaveChangesAsync();
        }
        await _db.CodigosEntrega.Where(c => c.Id == code.Id && !c.Usado && c.Reservado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Usado, true).SetProperty(c => c.UsadoAt, now));
        var localB = _db.CodigosEntrega.Local.FirstOrDefault(c => c.Id == code.Id);
        if (localB is not null) await _db.Entry(localB).ReloadAsync();
        orden.Paradas = await _db.ParadasEntrega.AsNoTracking().Where(p => p.ClienteAppId == orden.ClienteAppId
            && p.OrdenEntregaId == orden.Id).ToListAsync();
        EnvirapidoOperationsService.ReconcileManagedOrder(orden, now);
        await _db.OrdenesEntrega.Where(o => o.Id == orden.Id && o.ClienteAppId == orden.ClienteAppId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Estado, orden.Estado).SetProperty(o => o.PilotoId, orden.PilotoId)
                .SetProperty(o => o.RecolectadaAt, o => o.RecolectadaAt ?? now));
        await RefrescarOrdenLocalAsync(orden.Id);
        var result = new RecogidaParadasResult(orden.Id, EstadoOrden.Recolectada.ToString(), now,
            requested.Select(p => new RecogidaParadaConfirmadaDto(p.ParadaId, p.RevisionAsignacion!, now)).ToArray());
        GuardarConfirmacion(orden.ClienteAppId, orden.Id, actorIdentityUserId, "RecogerParadas", clave, hash, result);
        await _db.SaveChangesAsync(); await tx.CommitAsync(); return result;
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* Preserve the original uncertain failure. */ }
            _db.ChangeTracker.Clear(); throw;
        }
    }

    private async Task<EntregaParadaResult> ConfirmarParadaGestionadaAsync(int ordenId, int paradaId,
        string codigoC, string actor, string operacionId, string? revisionAsignacion)
    {
        var clave = NormalizarOperacionId(operacionId) ?? throw new ArgumentException("OperacionId es obligatorio.");
        // A previously committed cap3 C may be replayed after conversion. It is
        // still an actor-scoped receipt; a new managed mutation requires revision.
        var hash = revisionAsignacion is null ? Hash(JsonSerializer.Serialize(new { ordenId, paradaId, codigoC }))
            : Hash(JsonSerializer.Serialize(new { ordenId, paradaId, codigoC, revisionAsignacion }));
        var piloto = await PilotoActivoGestionadoAsync(actor);
        using var tx = await _quotas.BeginTenantMutationAsync(piloto.ClienteAppId);
        try
        {
        var lockedTenant = piloto.ClienteAppId;
        piloto = await PilotoActivoGestionadoAsync(actor);
        if (piloto.ClienteAppId != lockedTenant) throw new OperationsException(403, "El acceso del piloto cambió.");
        var orden = await OrdenGestionadaAsync(ordenId, piloto.ClienteAppId);
        var previous = await OperacionConfirmacionAsync(orden.ClienteAppId, actor, "ConfirmarParada", clave, hash);
        if (previous is not null)
        {
            var ack = JsonSerializer.Deserialize<EntregaParadaResult>(previous.ResultadoJson!)!;
            await tx.CommitAsync(); return ack;
        }
        var parada = await _db.ParadasEntrega.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paradaId
            && p.OrdenEntregaId == orden.Id && p.ClienteAppId == orden.ClienteAppId);
        if (parada is null) throw new RejectedOperationException("La parada no está disponible.", 409);
        ExigirRevision(parada, revisionAsignacion);
        if (!orden.GestionPorParadas || EstadosEntrega.EsTerminal(orden.Estado) || parada.EnReasignacion
            || parada.PilotoAsignadoId != piloto.Id || parada.PilotoCustodioId != piloto.Id
            || !parada.RecogidaParadaAt.HasValue || parada.Estado != EstadoParadaEntrega.Pendiente)
            throw new RejectedOperationException("La parada no está bajo esta asignación y custodia para entregar.", 409);
        await new OperationsWorkGuard(_db, _clock).ValidateAsync(orden.ClienteAppId, orden.Id, parada.Id, false);
        var code = await _db.CodigosEntrega.AsNoTracking().SingleOrDefaultAsync(c => c.ClienteAppId == orden.ClienteAppId
            && c.OrdenEntregaId == orden.Id && c.ParadaEntregaId == parada.Id && c.Tipo == TipoCodigo.C_Finalizacion
            && c.Codigo == codigoC && !c.Usado && c.Reservado);
        if (code is null) throw new OrdenSolicitudRechazadaException("Código C inválido para esta parada.");
        var now = _clock.GetUtcNow().UtcDateTime;
        var changed = await _db.ParadasEntrega.Where(p => p.Id == parada.Id && p.ClienteAppId == orden.ClienteAppId
            && p.OrdenEntregaId == orden.Id && p.Estado == EstadoParadaEntrega.Pendiente && p.PilotoEntregaId == null
            && p.RevisionAsignacion == revisionAsignacion && p.PilotoAsignadoId == piloto.Id && p.PilotoCustodioId == piloto.Id
            && p.RecogidaParadaAt != null && !p.EnReasignacion)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoParadaEntrega.Entregada).SetProperty(p => p.EntregadaAt, now)
                .SetProperty(p => p.PilotoEntregaId, piloto.Id).SetProperty(p => p.PilotoCustodioId, (int?)null));
        if (changed != 1) throw new RejectedOperationException("La asignación o custodia cambió antes de entregar.", 409);
        var consumed = await _db.CodigosEntrega.Where(c => c.Id == code.Id && !c.Usado && c.Reservado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Usado, true).SetProperty(c => c.UsadoAt, now).SetProperty(c => c.Reservado, false));
        if (consumed != 1) throw new RejectedOperationException("La confirmación del código cambió. Actualiza la entrega.", 409);
        var localStop = _db.ParadasEntrega.Local.FirstOrDefault(p => p.Id == parada.Id);
        if (localStop is not null) await _db.Entry(localStop).ReloadAsync();
        var localCode = _db.CodigosEntrega.Local.FirstOrDefault(c => c.Id == code.Id);
        if (localCode is not null) await _db.Entry(localCode).ReloadAsync();
        var stops = await _db.ParadasEntrega.AsNoTracking().Where(p => p.ClienteAppId == orden.ClienteAppId
            && p.OrdenEntregaId == orden.Id).ToListAsync();
        var allClosed = stops.All(p => EstadosEntrega.EsTerminal(p.Estado));
        orden.Paradas = stops;
        EnvirapidoOperationsService.ReconcileManagedOrder(orden, now);
        var parent = orden.Estado;
        await _db.OrdenesEntrega.Where(o => o.Id == orden.Id && o.ClienteAppId == orden.ClienteAppId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Estado, parent).SetProperty(o => o.PilotoId, orden.PilotoId)
                .SetProperty(o => o.FinalizadaAt, orden.FinalizadaAt).SetProperty(o => o.EntregadaAt, orden.EntregadaAt));
        if (allClosed)
            foreach (var c in await _db.CodigosEntrega.Where(c => c.ClienteAppId == orden.ClienteAppId && c.OrdenEntregaId == orden.Id).ToListAsync()) c.Reservado = false;
        await RefrescarOrdenLocalAsync(orden.Id);
        var own = stops.Where(p => p.PilotoAsignadoId == piloto.Id && !p.EnReasignacion || p.PilotoEntregaId == piloto.Id).ToArray();
        var completed = !own.Any(p => p.PilotoAsignadoId == piloto.Id && !p.EnReasignacion && !EstadosEntrega.EsTerminal(p.Estado));
        RegistrarHistorial(orden.Id, TipoEventoOrden.Entregada, actor, "Piloto",
            $"Entrega de paradaId={parada.Id}, revisión={parada.RevisionAsignacion}, confirmada con C por pilotoId={piloto.Id}.");
        var result = new EntregaParadaResult(orden.Id, parada.Id, EstadoParadaEntrega.Entregada.ToString(), parent.ToString(),
            now, own.Count(p => p.Estado == EstadoParadaEntrega.Entregada), own.Length) { TrabajoPilotoFinalizado = completed };
        GuardarConfirmacion(orden.ClienteAppId, orden.Id, actor, "ConfirmarParada", clave, hash, result);
        await _db.SaveChangesAsync();
        await new OperationsWorkGuard(_db, _clock).ReconcileCompletedRoutesAsync(orden.ClienteAppId, orden.Id);
        await tx.CommitAsync(); return result;
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* Preserve the original uncertain failure. */ }
            _db.ChangeTracker.Clear(); throw;
        }
    }
}
