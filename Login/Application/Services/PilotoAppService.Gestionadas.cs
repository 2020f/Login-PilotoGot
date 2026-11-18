using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;

namespace Login.Application.Services;

public partial class PilotoAppService
{
    private static OrdenPilotoDto ToDtoGestionada(OrdenEntrega order, int pilotoId, OperationsWorkGuard.ReadPolicy? policy = null)
    {
        var own = order.Paradas.Where(p => p.PilotoAsignadoId == pilotoId && !p.EnReasignacion
            || p.PilotoEntregaId == pilotoId).OrderBy(p => p.Posicion).ToArray();
        var active = own.Where(p => p.PilotoAsignadoId == pilotoId && !p.EnReasignacion && !EstadosEntrega.EsTerminal(p.Estado)).ToArray();
        var state = active.Length > 0 ? active.Any(p => p.PilotoCustodioId == pilotoId && p.RecogidaParadaAt.HasValue)
            ? EstadoOrden.Recolectada : EstadoOrden.Asignada
            : own.Length > 0 && own.All(p => p.Estado == EstadoParadaEntrega.Entregada) ? EstadoOrden.Entregada
            : own.Length > 0 && own.All(p => p.Estado == EstadoParadaEntrega.Cancelada) ? EstadoOrden.Cancelada
            : own.Length > 0 && own.All(p => p.Estado == EstadoParadaEntrega.Devuelta) ? EstadoOrden.Devuelta : EstadoOrden.FinalizadaConDevoluciones;
        var finished = active.Length == 0 ? own.Select(p => p.EntregadaAt ?? p.DevueltaAt).Where(t => t.HasValue)
            .Select(t => t!.Value).DefaultIfEmpty().Max() : default;
        return new(order.Id, order.NumeroOrdenA, state.ToString(), order.CreatedAt, false,
            order.DatosEntregaCongelados ? order.RecogidaNombre : order.Tienda.Nombre,
            order.DatosEntregaCongelados ? order.RecogidaDireccion : order.Tienda.Direccion,
            order.DatosEntregaCongelados ? order.RecogidaTelefono : order.Tienda.Telefono,
            null, "", "", null, "", null, null,
            order.RecogidaLatitud, order.RecogidaLongitud, order.CodigoA)
        {
            GestionPorParadas = true, FinalizadaAt = finished == default ? null : finished,
            PuedeRecoger = false, BloqueoRecogida = "Selecciona las paradas de la recogida por lote.",
            Paradas = own.Select(stop =>
            {
                var visible = (stop.PilotoCustodioId == pilotoId && stop.RecogidaParadaAt.HasValue)
                    || stop.PilotoEntregaId == pilotoId && stop.Estado == EstadoParadaEntrega.Entregada;
                var pickupBlock = stop.PilotoAsignadoId != pilotoId || stop.EnReasignacion || stop.Estado != EstadoParadaEntrega.Pendiente
                    || stop.RecogidaParadaAt.HasValue ? "La parada no está pendiente de recogida."
                    : stop.PilotoCustodioId.HasValue && stop.PilotoCustodioId != pilotoId
                        ? "Requiere confirmar primero la recepción del traspaso."
                        : policy?.MotivoBloqueo(order.Id, stop.Id, true);
                var deliveryBlock = stop.PilotoAsignadoId != pilotoId || stop.EnReasignacion || stop.Estado != EstadoParadaEntrega.Pendiente
                    ? "La parada no está pendiente bajo esta asignación."
                    : stop.PilotoCustodioId != pilotoId || !stop.RecogidaParadaAt.HasValue
                        ? "Confirma primero la recogida o recepción de esta parada."
                        : policy?.MotivoBloqueo(order.Id, stop.Id, false);
                return new ParadaPilotoDto(stop.Id, stop.UsuarioFinalId, stop.Estado.ToString(), stop.EntregadaAt,
                    visible ? stop.Nombre : "", visible ? stop.Direccion : null, visible ? stop.Telefono : null,
                    visible ? stop.MapaLink : null, visible ? stop.Latitud : null, visible ? stop.Longitud : null,
                    visible ? stop.Instrucciones : null)
                {
                    DevueltaAt = stop.DevueltaAt, RevisionAsignacion = stop.RevisionAsignacion,
                    PuedeVerDestino = visible, RecogidaAt = stop.RecogidaParadaAt,
                    PuedeRecoger = policy is not null && pickupBlock is null, BloqueoRecogida = pickupBlock,
                    PuedeConfirmarEntrega = policy is not null && deliveryBlock is null, BloqueoEntrega = deliveryBlock,
                    RecogidaNombre = stop.RecogidaActualNombre ?? order.RecogidaNombre,
                    RecogidaDireccion = stop.RecogidaActualDireccion ?? order.RecogidaDireccion,
                    RecogidaLatitud = stop.RecogidaActualLatitud ?? order.RecogidaLatitud,
                    RecogidaLongitud = stop.RecogidaActualLongitud ?? order.RecogidaLongitud
                };
            }).ToArray()
        };
    }
}
