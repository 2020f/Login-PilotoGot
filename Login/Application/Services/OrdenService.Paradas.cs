using System.Security.Cryptography;
using System.Text;
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
    partial void ValidarTrabajoOperativo(int tenant, int orden, int? parada, bool recogida, ref Task validacion);
    partial void ValidarAsignacionOperativa(int tenant, int orden, int piloto, ref Task validacion);
    partial void FiltrarOfertasOperativas(ref IQueryable<OrdenEntrega> query);
    partial void FinalizarRutasOperativas(int tenant, int orden, ref Task finalizacion);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static List<OrdenDestinatarioRequest> NormalizarDestinatarios(int usuarioFinalId,
        IReadOnlyList<OrdenDestinatarioRequest>? destinatarios)
    {
        if (destinatarios is { Count: 0 }) throw new ArgumentException("La orden necesita un destinatario.");
        var lista = destinatarios?.Select(d => new OrdenDestinatarioRequest(d.UsuarioFinalId,
            string.IsNullOrWhiteSpace(d.Instrucciones) ? null : d.Instrucciones.Trim())).ToList()
            ?? new List<OrdenDestinatarioRequest> { new(usuarioFinalId) };
        if (lista.Any(d => d.UsuarioFinalId <= 0 || d.Instrucciones?.Length > 400))
            throw new ArgumentException("Destinatario inválido o instrucciones superiores a 400 caracteres.");
        if (destinatarios is not null && usuarioFinalId > 0 && usuarioFinalId != lista[0].UsuarioFinalId)
            throw new ArgumentException("El destinatario simple debe coincidir con el primero de la lista.");
        return lista;
    }

    private static OrdenCreadaResult ResultadoCreacion(OrdenEntrega orden)
    {
        var paradas = orden.Paradas.OrderBy(p => p.Posicion).Select(p => new ParadaCreadaDto(p.Id,
            p.UsuarioFinalId, p.Nombre, orden.Codigos.Single(c => c.ParadaEntregaId == p.Id
                && c.Tipo == TipoCodigo.C_Finalizacion).Codigo, p.Direccion)).ToList();
        return new OrdenCreadaResult(orden.Id, orden.NumeroOrdenA,
            orden.Codigos.Single(c => c.Tipo == TipoCodigo.B_Recoleccion).Codigo,
            orden.Paradas.Count <= 1 ? orden.Codigos.Single(c => c.Tipo == TipoCodigo.C_Finalizacion).Codigo : null,
            orden.CodigoA) { Paradas = paradas };
    }

    private async Task<OrdenEntrega> CargarOrdenAutorizadaAsync(int ordenId, string actor)
    {
        var orden = await _db.OrdenesEntrega.AsNoTracking().SingleOrDefaultAsync(o => o.Id == ordenId
            && o.Piloto != null && o.Piloto.IdentityUserId == actor && o.Piloto.Activo
            && o.Piloto.ClienteAppId == o.ClienteAppId && o.ClienteApp.Estado == EstadoClienteApp.Activo);
        if (orden is null) throw new InvalidOperationException("La orden no pertenece a este piloto activo.");
        if (orden.GestionPorParadas) throw new OperationsException(426, "Esta orden requiere acciones con la revisión de cada parada.");
        return orden;
    }

    private async Task<OperacionOrden?> OperacionConfirmacionAsync(int tenant, string actor, string tipo,
        string? clave, string hash)
    {
        if (clave is null) return null;
        var op = await _db.OperacionesOrden.AsNoTracking().SingleOrDefaultAsync(o => o.ClienteAppId == tenant
            && o.ActorIdentityUserId == actor && o.Tipo == tipo && o.Clave == clave);
        if (op is not null && op.PayloadHash != hash)
            throw new OperacionOrdenConflictoException("La operación ya existe con otro contenido.");
        return op;
    }

    private void GuardarConfirmacion(int tenant, int orden, string actor, string tipo, string? clave,
        string hash, object resultado)
    {
        if (clave is not null) _db.OperacionesOrden.Add(new OperacionOrden { ClienteAppId = tenant,
            OrdenEntregaId = orden, ActorIdentityUserId = actor, Tipo = tipo, Clave = clave,
            PayloadHash = hash, ResultadoJson = JsonSerializer.Serialize(resultado) });
    }

    public async Task<EntregaParadaResult> ConfirmarEntregaParadaAsync(int ordenId, int paradaId,
        string codigoC, string actorIdentityUserId, string? operacionId = null, string? revisionAsignacion = null)
    {
        if (operacionId is null) throw new ArgumentException("OperacionId es obligatorio para confirmar una parada.");
        if (await _db.OrdenesEntrega.AsNoTracking().AnyAsync(o => o.Id == ordenId && o.GestionPorParadas
            && _db.Pilotos.Any(p => p.IdentityUserId == actorIdentityUserId && p.ClienteAppId == o.ClienteAppId
                && p.Activo && p.ClienteApp.Estado == EstadoClienteApp.Activo)))
            return await ConfirmarParadaGestionadaAsync(ordenId, paradaId, codigoC, actorIdentityUserId,
                operacionId, revisionAsignacion);
        return await ConfirmarParadaCoreAsync(ordenId, paradaId, codigoC, actorIdentityUserId, operacionId);
    }

    private async Task<EntregaParadaResult> ConfirmarParadaCoreAsync(int ordenId, int paradaId,
        string codigoC, string actor, string? operacionId)
    {
        var clave = NormalizarOperacionId(operacionId);
        var orden = await CargarOrdenAutorizadaAsync(ordenId, actor);
        if (orden.GestionPorParadas) throw new OperationsException(426, "Actualiza la app para gestionar esta asignación por parada.");
        using var tx = await _quotas.BeginTenantMutationAsync(orden.ClienteAppId);
        orden = await CargarOrdenAutorizadaAsync(ordenId, actor);
        if (orden.GestionPorParadas) throw new OperationsException(426, "Actualiza la app para gestionar esta asignación por parada.");
        var hash = Hash(JsonSerializer.Serialize(new { ordenId, paradaId, codigoC }));
        var op = await OperacionConfirmacionAsync(orden.ClienteAppId, actor, "ConfirmarParada", clave, hash);
        if (op is not null)
        {
            var resultado = JsonSerializer.Deserialize<EntregaParadaResult>(op.ResultadoJson!)!;
            await tx.CommitAsync(); return resultado;
        }
        var parada = await _db.ParadasEntrega.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paradaId
            && p.OrdenEntregaId == orden.Id && p.ClienteAppId == orden.ClienteAppId);
        if (parada is null) throw new OrdenSolicitudRechazadaException("La parada no pertenece a esta orden.");
        if (orden.Estado != EstadoOrden.Recolectada || parada.Estado != EstadoParadaEntrega.Pendiente)
            throw new OrdenSolicitudRechazadaException("La parada no está pendiente de entrega o falta confirmar la recogida.");
        var trabajo = Task.CompletedTask;
        ValidarTrabajoOperativo(orden.ClienteAppId, orden.Id, parada.Id, false, ref trabajo);
        await trabajo;
        var codeC = await _db.CodigosEntrega.SingleOrDefaultAsync(c => c.ClienteAppId == orden.ClienteAppId
            && c.OrdenEntregaId == orden.Id && c.ParadaEntregaId == parada.Id
            && c.Tipo == TipoCodigo.C_Finalizacion && c.Codigo == codigoC && !c.Usado && c.Reservado);
        if (codeC is null) throw new OrdenSolicitudRechazadaException("Código C inválido para esta parada.");
        var now = DateTime.UtcNow;
        var rows = await _db.ParadasEntrega.Where(p => p.Id == parada.Id && p.ClienteAppId == orden.ClienteAppId
            && p.OrdenEntregaId == orden.Id && p.Estado == EstadoParadaEntrega.Pendiente)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoParadaEntrega.Entregada)
                .SetProperty(p => p.EntregadaAt, now));
        if (rows != 1) throw new InvalidOperationException("La parada ya cambió de estado.");
        codeC.Usado = true; codeC.UsadoAt = now; codeC.Reservado = false;
        var total = await _db.ParadasEntrega.CountAsync(p => p.OrdenEntregaId == orden.Id && p.ClienteAppId == orden.ClienteAppId);
        var entregadas = await _db.ParadasEntrega.CountAsync(p => p.OrdenEntregaId == orden.Id
            && p.ClienteAppId == orden.ClienteAppId && p.Estado == EstadoParadaEntrega.Entregada);
        var cerradas = await _db.ParadasEntrega.CountAsync(p => p.OrdenEntregaId == orden.Id
            && p.ClienteAppId == orden.ClienteAppId && EstadosEntrega.ParadasTerminales.Contains(p.Estado));
        var final = total == cerradas;
        var estadoPadre = final ? entregadas == total ? EstadoOrden.Entregada : EstadoOrden.FinalizadaConDevoluciones : EstadoOrden.Recolectada;
        if (final)
        {
            await _db.OrdenesEntrega.Where(o => o.Id == orden.Id && o.Estado == EstadoOrden.Recolectada)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Estado, estadoPadre).SetProperty(o => o.FinalizadaAt, now)
                    .SetProperty(o => o.EntregadaAt, estadoPadre == EstadoOrden.Entregada ? now : (DateTime?)null));
            foreach (var code in await _db.CodigosEntrega.Where(c => c.OrdenEntregaId == orden.Id).ToListAsync())
                code.Reservado = false;
            await RefrescarOrdenLocalAsync(orden.Id);
        }
        RegistrarHistorial(orden.Id, TipoEventoOrden.Entregada, actor, "Piloto",
            $"Entrega de paradaId={parada.Id} confirmada con C. Progreso {entregadas}/{total}.");
        var result = new EntregaParadaResult(orden.Id, parada.Id, "Entregada", estadoPadre.ToString(), now, entregadas, total);
        GuardarConfirmacion(orden.ClienteAppId, orden.Id, actor, "ConfirmarParada", clave, hash, result);
        await _db.SaveChangesAsync();
        var finalizacion = Task.CompletedTask;
        FinalizarRutasOperativas(orden.ClienteAppId, orden.Id, ref finalizacion);
        await finalizacion;
        await tx.CommitAsync();
        return result;
    }

    // Compatibilidad de órdenes antiguas sin materialización (por ejemplo fixtures de legado).
    // La migración hace este backfill masivamente sin cambiar códigos emitidos.
    private async Task<ParadaEntrega> AsegurarParadaLegadaAsync(OrdenEntrega orden)
    {
        var paradas = await _db.ParadasEntrega.Where(p => p.OrdenEntregaId == orden.Id).ToListAsync();
        if (paradas.Count > 1) throw new InvalidOperationException("Actualiza la app: esta orden tiene varias paradas.");
        if (paradas.Count == 1) return paradas[0];
        var origen = await _db.UsuariosFinales.AsNoTracking().SingleAsync(u => u.Id == orden.UsuarioFinalId);
        var p = new ParadaEntrega { ClienteAppId = orden.ClienteAppId, OrdenEntregaId = orden.Id, TiendaId = orden.TiendaId,
            UsuarioFinalId = origen.Id, Posicion = 1, Nombre = orden.DatosEntregaCongelados ? orden.DestinoNombre : origen.Nombre,
            Direccion = orden.DatosEntregaCongelados ? orden.DestinoDireccion : origen.DireccionUbicacion,
            Telefono = orden.DatosEntregaCongelados ? orden.DestinoTelefono : origen.Telefono,
            MapaLink = orden.DatosEntregaCongelados ? orden.DestinoMapaLink : origen.MapaLink,
            Latitud = orden.DatosEntregaCongelados ? orden.DestinoLatitud : origen.Latitud,
            Longitud = orden.DatosEntregaCongelados ? orden.DestinoLongitud : origen.Longitud, CreatedAt = orden.CreatedAt,
            Estado = orden.Estado == EstadoOrden.Entregada ? EstadoParadaEntrega.Entregada : EstadoParadaEntrega.Pendiente,
            EntregadaAt = orden.EntregadaAt };
        _db.ParadasEntrega.Add(p); await _db.SaveChangesAsync();
        var code = await _db.CodigosEntrega.SingleAsync(c => c.OrdenEntregaId == orden.Id && c.Tipo == TipoCodigo.C_Finalizacion);
        code.ParadaEntregaId = p.Id; await _db.SaveChangesAsync();
        return p;
    }
}
