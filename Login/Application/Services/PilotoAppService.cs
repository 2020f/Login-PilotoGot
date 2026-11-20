using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services
{
    public partial class PilotoAppService : IPilotoAppService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOrdenService _ordenService;
        private readonly QuotaPolicyService _quotas;
        private readonly TimeProvider _clock;

        public PilotoAppService(ApplicationDbContext db, IOrdenService ordenService, QuotaPolicyService quotas, TimeProvider? clock = null)
        {
            _db = db;
            _ordenService = ordenService;
            _quotas = quotas;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<OrdenPilotoDto?> GetOrdenActualAsync(string identityUserId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return null;

            var orden = await OrdenesActivasQuery(piloto.Id, piloto.ClienteAppId)
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt).FirstOrDefaultAsync();
            return orden is null ? null : (await MapActivasAsync(piloto, [orden])).Single();
        }

        public async Task<List<OrdenPilotoDto>> GetOrdenesAsync(string identityUserId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return new List<OrdenPilotoDto>();

            var ordenes = await OrdenesActivasQuery(piloto.Id, piloto.ClienteAppId)
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt).ToListAsync();
            return await MapActivasAsync(piloto, ordenes);
        }

        public async Task<OrdenPilotoDto?> GetOrdenDetalleAsync(string identityUserId, int ordenId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return null;

            var orden = await OrdenesActivasQuery(piloto.Id, piloto.ClienteAppId).FirstOrDefaultAsync(o => o.Id == ordenId);
            return orden is null ? null : (await MapActivasAsync(piloto, [orden])).Single();
        }

        public Task ConfirmarRecolectaAsync(string identityUserId, string codigoB, int? ordenId = null, string? operacionId = null)
            => _ordenService.ConfirmarRecolectaAsync(codigoB, identityUserId, "Piloto", ordenId, operacionId);

        public Task<EntregaParadaResult> ConfirmarEntregaParadaAsync(string identityUserId, int ordenId, int paradaId,
            string codigoC, string? operacionId = null, string? revisionAsignacion = null)
            => _ordenService.ConfirmarEntregaParadaAsync(ordenId, paradaId, codigoC, identityUserId, operacionId, revisionAsignacion);

        public Task<RecogidaParadasResult> ConfirmarRecogidaParadasAsync(string identityUserId, int ordenId,
            string codigoB, IReadOnlyList<RevisionParadaRequest> paradas, string operacionId)
            => _ordenService.ConfirmarRecogidaParadasAsync(ordenId, codigoB, paradas, identityUserId, operacionId);

        public async Task ConfirmarEntregaAsync(string identityUserId, string codigoB, string codigoC, int? ordenId = null)
        {
            if (string.IsNullOrWhiteSpace(codigoB) && !ordenId.HasValue) throw new ArgumentException("Selecciona la orden para confirmar su entrega.");
            if (string.IsNullOrWhiteSpace(codigoC)) throw new ArgumentException("codigoC requerido.");

            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");
            if (ordenId.HasValue && await _db.OrdenesEntrega.AnyAsync(o => o.Id == ordenId
                && o.ClienteAppId == piloto.ClienteAppId && o.GestionPorParadas))
                throw new OperationsException(426, "Confirma la parada con su revisión de asignación vigente.");

            // Validación dura: debe ser su orden y estar RECOLECTADA
            var ordenValida = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Codigos)
                .AnyAsync(o =>
                    o.PilotoId == piloto.Id &&
                    o.ClienteAppId == piloto.ClienteAppId &&
                    o.Estado == EstadoOrden.Recolectada &&
                    (ordenId == null || o.Id == ordenId) &&
                    ((ordenId.HasValue && string.IsNullOrWhiteSpace(codigoB)) ||
                     o.Codigos.Any(c => c.Tipo == TipoCodigo.B_Recoleccion && c.Codigo == codigoB)));

            if (!ordenValida)
                throw new InvalidOperationException("No puedes cerrar: la orden no está recolectada o no es tuya.");

            await _ordenService.ConfirmarEntregaAsync(codigoB, codigoC, identityUserId, "Piloto", ordenId);
        }

        // ---------- Auto-asignación ----------
        public async Task<PaginaOrdenesPilotoDto> GetHistorialAsync(string identityUserId, int pagina = 1, int tamano = 30, bool incluirGestionadas = true)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) throw new InvalidOperationException("Piloto no disponible.");
            pagina = Math.Clamp(pagina, 1, 1000000); tamano = Math.Clamp(tamano, 1, 100);
            var query = HistoricasQuery(piloto.Id, piloto.ClienteAppId, incluirGestionadas);
            var total = await query.CountAsync();
            var ordenes = await query.OrderByDescending(o => o.FinalizadaAt ?? o.EntregadaAt).ThenByDescending(o => o.Id)
                .Skip((pagina - 1) * tamano).Take(tamano).ToListAsync();
            return new PaginaOrdenesPilotoDto(ordenes.Select(o => o.GestionPorParadas ? ToDtoGestionada(o, piloto.Id) : ToDto(o)).ToList(), pagina, total, (int)Math.Ceiling(total / (double)tamano));
        }

        public async Task<OrdenPilotoDto?> GetOrdenHistoricaAsync(string identityUserId, int ordenId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return null;
            var orden = await HistoricasQuery(piloto.Id, piloto.ClienteAppId).FirstOrDefaultAsync(o => o.Id == ordenId);
            return orden is null ? null : orden.GestionPorParadas ? ToDtoGestionada(orden, piloto.Id) : ToDto(orden);
        }

        private IQueryable<OrdenEntrega> HistoricasQuery(int pilotoId, int tenant, bool incluirGestionadas = true) => _db.OrdenesEntrega.AsNoTracking()
            .Include(o => o.Tienda).Include(o => o.UsuarioFinal).Include(o => o.Paradas).AsSplitQuery()
            .Where(o => o.ClienteAppId == tenant && (!o.GestionPorParadas
                ? o.PilotoId == pilotoId && EstadosEntrega.Terminales.Contains(o.Estado)
                : incluirGestionadas && o.Paradas.Any(p => p.PilotoEntregaId == pilotoId
                    || p.PilotoAsignadoId == pilotoId && EstadosEntrega.ParadasTerminales.Contains(p.Estado))
                    && !o.Paradas.Any(p => p.PilotoAsignadoId == pilotoId && !p.EnReasignacion
                        && !EstadosEntrega.ParadasTerminales.Contains(p.Estado))));


        public async Task<List<OrdenAutoDisponibleDto>> GetOrdenesAutoAsync(string identityUserId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return new List<OrdenAutoDisponibleDto>();

            return await _ordenService.GetOrdenesAutoDisponiblesAsync(piloto.Id);
        }

        public async Task AceptarOrdenAutoAsync(string identityUserId, int ordenId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            await _ordenService.AceptarOrdenAutoAsync(ordenId, piloto.Id, identityUserId, "Piloto");
        }

        public async Task RechazarOrdenAutoAsync(string identityUserId, int ordenId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            await _ordenService.RechazarOrdenAutoAsync(ordenId, piloto.Id);
        }

        public async Task ActualizarUbicacionAsync(string identityUserId, double latitud, double longitud)
        {
            if (latitud < -90 || latitud > 90 || longitud < -180 || longitud > 180)
                throw new ArgumentException("Coordenadas inválidas.");

            var piloto = await _db.Pilotos
                .SingleOrDefaultAsync(p => p.IdentityUserId == identityUserId && p.Activo);

            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            piloto.Latitud = latitud;
            piloto.Longitud = longitud;
            piloto.UltimaUbicacionAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task<PilotoEstadoDto> GetEstadoAsync(string identityUserId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            var ocupadas = await TieneOrdenesAbiertasAsync(piloto.Id, piloto.ClienteAppId);
            return new PilotoEstadoDto(piloto.EnLinea,
                (ocupadas ? EstadoPiloto.Ocupado : EstadoPiloto.Disponible).ToString());
        }

        public async Task SetEnLineaAsync(string identityUserId, bool enLinea)
        {
            // Mismo orden de bloqueo que las altas, bajas y asignaciones.
            var tenantId = await _db.Pilotos.AsNoTracking().Where(p => p.IdentityUserId == identityUserId && p.Activo)
                .Select(p => p.ClienteAppId).SingleOrDefaultAsync();
            if (tenantId == 0) throw new InvalidOperationException("Piloto no encontrado o inactivo.");
            using var tx = await _quotas.BeginTenantMutationAsync(tenantId);
            var piloto = await _db.Pilotos
                .SingleOrDefaultAsync(p => p.IdentityUserId == identityUserId && p.ClienteAppId == tenantId && p.Activo);

            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            piloto.EnLinea = enLinea;

            piloto.Estado = await TieneOrdenesAbiertasAsync(piloto.Id, tenantId)
                ? EstadoPiloto.Ocupado : EstadoPiloto.Disponible;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        private Task<bool> TieneOrdenesAbiertasAsync(int pilotoId, int tenantId)
            => _db.OrdenesEntrega.AnyAsync(o => o.ClienteAppId == tenantId && !EstadosEntrega.Terminales.Contains(o.Estado)
                && (!o.GestionPorParadas ? o.PilotoId == pilotoId : o.Paradas.Any(p =>
                    !EstadosEntrega.ParadasTerminales.Contains(p.Estado) && (p.PilotoCustodioId == pilotoId
                        || p.PilotoAsignadoId == pilotoId && !p.EnReasignacion))));

        // ---------- Helpers ----------

        private IQueryable<OrdenEntrega> OrdenesActivasQuery(int pilotoId, int clienteAppId)
        {
            return _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Tienda)
                .Include(o => o.UsuarioFinal)
                .Include(o => o.Paradas).AsSplitQuery()
                .Where(o => o.ClienteAppId == clienteAppId && (!o.GestionPorParadas
                    ? o.PilotoId == pilotoId && (o.Estado == EstadoOrden.Asignada || o.Estado == EstadoOrden.Recolectada)
                    : !EstadosEntrega.Terminales.Contains(o.Estado) && o.Paradas.Any(p => p.PilotoAsignadoId == pilotoId
                        && !p.EnReasignacion && !EstadosEntrega.ParadasTerminales.Contains(p.Estado))));
        }

        private async Task<List<OrdenPilotoDto>> MapActivasAsync(Piloto piloto, List<OrdenEntrega> orders)
        {
            var policy = await new OperationsWorkGuard(_db, _clock).LoadReadPolicyAsync(piloto.ClienteAppId,
                orders.Select(o => o.Id).ToArray());
            return orders.Select(o => o.GestionPorParadas ? ToDtoGestionada(o, piloto.Id, policy) : ToDto(o, policy)).ToList();
        }

        private static OrdenPilotoDto ToDto(OrdenEntrega o, OperationsWorkGuard.ReadPolicy? policy = null)
        {
            var puedeVerDestino = o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada
                || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones;
            var bloqueoRecogida = o.Estado != EstadoOrden.Asignada || o.RecolectadaAt.HasValue
                ? "La orden no está pendiente de recogida." : policy?.MotivoBloqueo(o.Id, null, true);
            return new OrdenPilotoDto(
                o.Id,
                o.NumeroOrdenA,
                o.Estado.ToString(),
                o.CreatedAt,
                (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones),
                o.DatosEntregaCongelados ? o.RecogidaNombre : o.Tienda.Nombre,
                o.DatosEntregaCongelados ? o.RecogidaDireccion : o.Tienda.Direccion,
                o.DatosEntregaCongelados ? o.RecogidaTelefono : o.Tienda.Telefono,
                o.NotaPedido,
                puedeVerDestino ? (o.DatosEntregaCongelados ? o.DestinoNombre : o.UsuarioFinal?.Nombre ?? "") : "",
                (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? (o.DatosEntregaCongelados ? o.DestinoDireccion : o.UsuarioFinal.DireccionUbicacion) : "",
                (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? (o.DatosEntregaCongelados ? o.DestinoMapaLink : o.UsuarioFinal.MapaLink) : null,
                (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? (o.DatosEntregaCongelados ? o.DestinoTelefono : o.UsuarioFinal.Telefono) : "",
                o.Precio,
                o.DistanciaKm,
                o.DatosEntregaCongelados ? o.RecogidaLatitud : o.Tienda.Latitud,
                o.DatosEntregaCongelados ? o.RecogidaLongitud : o.Tienda.Longitud,
                o.CodigoA) { FinalizadaAt = o.FinalizadaAt,
                PuedeRecoger = policy is not null && bloqueoRecogida is null, BloqueoRecogida = bloqueoRecogida,
                Paradas = o.Paradas.OrderBy(p => p.Posicion).Select(p => new ParadaPilotoDto(p.Id,
                    p.UsuarioFinalId, p.Estado.ToString(), p.EntregadaAt,
                    puedeVerDestino ? p.Nombre : "", (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? p.Direccion : null,
                    (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? p.Telefono : null,
                    (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? p.MapaLink : null,
                    (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? p.Latitud : null,
                    (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? p.Longitud : null,
                    (o.RecolectadaAt.HasValue || o.Estado == EstadoOrden.Entregada || o.Estado == EstadoOrden.Devuelta || o.Estado == EstadoOrden.FinalizadaConDevoluciones) ? p.Instrucciones : null) {
                        DevueltaAt = p.DevueltaAt, RevisionAsignacion = p.RevisionAsignacion,
                        PuedeVerDestino = puedeVerDestino, RecogidaAt = o.RecolectadaAt,
                        PuedeConfirmarEntrega = policy is not null && o.Estado == EstadoOrden.Recolectada
                            && o.RecolectadaAt.HasValue && p.Estado == EstadoParadaEntrega.Pendiente
                            && policy.MotivoBloqueo(o.Id, p.Id, false) is null,
                        BloqueoEntrega = o.Estado != EstadoOrden.Recolectada || !o.RecolectadaAt.HasValue
                            ? "Confirma primero la recogida de esta orden."
                            : p.Estado != EstadoParadaEntrega.Pendiente ? "La parada no está pendiente de entrega."
                            : policy?.MotivoBloqueo(o.Id, p.Id, false)
                    }).ToList()
                };
        }

        private async Task<Piloto?> GetPilotoLogueadoAsync(string identityUserId)
        {
            if (string.IsNullOrWhiteSpace(identityUserId))
                throw new InvalidOperationException("Usuario no autenticado.");

            return await _db.Pilotos.AsNoTracking()
                .SingleOrDefaultAsync(p => p.IdentityUserId == identityUserId && p.Activo);
        }
    }
}
