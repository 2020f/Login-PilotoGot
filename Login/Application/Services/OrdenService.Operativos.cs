using Login.Application.Operations;
using Login.Domain.Entities;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services;

public partial class OrdenService
{
    partial void ValidarTrabajoOperativo(int tenant, int orden, int? parada, bool recogida, ref Task validacion)
        => validacion = new OperationsWorkGuard(_db, _clock).ValidateAsync(tenant, orden, parada, recogida);
    partial void ValidarAsignacionOperativa(int tenant, int orden, int piloto, ref Task validacion)
        => validacion = new OperationsWorkGuard(_db, _clock).ValidateAssignmentAsync(tenant, orden, piloto);
    partial void FiltrarOfertasOperativas(ref IQueryable<OrdenEntrega> query)
        => query = query.Where(o => !_db.RutaOrdenesOperativas.Any(r => r.ClienteAppId == o.ClienteAppId
            && r.OrdenEntregaId == o.Id && r.Vigente) && !_db.DatosEnviosOperativos.Any(d => d.ClienteAppId == o.ClienteAppId
            && d.OrdenEntregaId == o.Id && (d.CanceladaAt != null || d.ProgramadaPara > _clock.GetUtcNow().UtcDateTime))
            && !_db.IncidenciasOperativas.Any(i => i.ClienteAppId == o.ClienteAppId && i.OrdenEntregaId == o.Id
                && i.Estado != EstadoIncidencia.Resuelta && (i.Efecto == EfectoIncidencia.PausarRuta
                    || i.Efecto == EfectoIncidencia.PausarParada && i.ParadaEntregaId == null)));
    partial void FinalizarRutasOperativas(int tenant, int orden, ref Task finalizacion)
        => finalizacion = new OperationsWorkGuard(_db, _clock).ReconcileCompletedRoutesAsync(tenant, orden);
}
