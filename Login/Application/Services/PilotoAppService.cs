using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services
{
    public class PilotoAppService : IPilotoAppService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOrdenService _ordenService;
        private readonly QuotaPolicyService _quotas;

        public PilotoAppService(ApplicationDbContext db, IOrdenService ordenService, QuotaPolicyService quotas)
        {
            _db = db;
            _ordenService = ordenService;
            _quotas = quotas;
        }

        public async Task<OrdenPilotoDto?> GetOrdenActualAsync(string identityUserId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return null;

            return await OrdenesActivasQuery(piloto.Id, piloto.ClienteAppId)
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt)
                .Select(ToDto())
                .FirstOrDefaultAsync();
        }

        public async Task<List<OrdenPilotoDto>> GetOrdenesAsync(string identityUserId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return new List<OrdenPilotoDto>();

            return await OrdenesActivasQuery(piloto.Id, piloto.ClienteAppId)
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt)
                .Select(ToDto())
                .ToListAsync();
        }

        public async Task<OrdenPilotoDto?> GetOrdenDetalleAsync(string identityUserId, int ordenId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return null;

            return await OrdenesActivasQuery(piloto.Id, piloto.ClienteAppId)
                .Where(o => o.Id == ordenId)
                .Select(ToDto())
                .FirstOrDefaultAsync();
        }

        public async Task ConfirmarRecolectaAsync(string identityUserId, string codigoB, int? ordenId = null)
        {
            if (string.IsNullOrWhiteSpace(codigoB))
                throw new ArgumentException("codigoB requerido.");

            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            // Validación dura: el B debe pertenecer a una orden ASIGNADA de este piloto
            var ordenValida = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Codigos)
                .AnyAsync(o =>
                    o.PilotoId == piloto.Id &&
                    o.ClienteAppId == piloto.ClienteAppId &&
                    o.Estado == EstadoOrden.Asignada &&
                    (ordenId == null || o.Id == ordenId) &&
                    o.Codigos.Any(c => c.Tipo == TipoCodigo.B_Recoleccion && c.Codigo == codigoB));

            if (!ordenValida)
                throw new InvalidOperationException("Ese B no corresponde a una orden asignada a ti.");

            await _ordenService.ConfirmarRecolectaAsync(codigoB, identityUserId, "Piloto", ordenId);
        }

        public async Task ConfirmarEntregaAsync(string identityUserId, string codigoB, string codigoC, int? ordenId = null)
        {
            if (string.IsNullOrWhiteSpace(codigoB)) throw new ArgumentException("codigoB requerido.");
            if (string.IsNullOrWhiteSpace(codigoC)) throw new ArgumentException("codigoC requerido.");

            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            // Validación dura: debe ser su orden y estar RECOLECTADA
            var ordenValida = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Codigos)
                .AnyAsync(o =>
                    o.PilotoId == piloto.Id &&
                    o.ClienteAppId == piloto.ClienteAppId &&
                    o.Estado == EstadoOrden.Recolectada &&
                    (ordenId == null || o.Id == ordenId) &&
                    o.Codigos.Any(c => c.Tipo == TipoCodigo.B_Recoleccion && c.Codigo == codigoB));

            if (!ordenValida)
                throw new InvalidOperationException("No puedes cerrar: la orden no está recolectada o no es tuya.");

            await _ordenService.ConfirmarEntregaAsync(codigoB, codigoC, identityUserId, "Piloto", ordenId);
        }

        // ---------- Auto-asignación ----------

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
            => _db.OrdenesEntrega.AnyAsync(o => o.ClienteAppId == tenantId && o.PilotoId == pilotoId
                && o.Estado != EstadoOrden.Entregada);

        // ---------- Helpers ----------

        private IQueryable<OrdenEntrega> OrdenesActivasQuery(int pilotoId, int clienteAppId)
        {
            return _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Tienda)
                .Include(o => o.UsuarioFinal)
                .Where(o => o.PilotoId == pilotoId && o.ClienteAppId == clienteAppId &&
                            (o.Estado == EstadoOrden.Asignada || o.Estado == EstadoOrden.Recolectada));
        }

        private static System.Linq.Expressions.Expression<Func<OrdenEntrega, OrdenPilotoDto>> ToDto()
        {
            return o => new OrdenPilotoDto(
                o.Id,
                o.NumeroOrdenA,
                o.Estado.ToString(),
                o.CreatedAt,
                o.Estado == EstadoOrden.Recolectada,
                o.DatosEntregaCongelados ? o.RecogidaNombre : o.Tienda.Nombre,
                o.DatosEntregaCongelados ? o.RecogidaDireccion : o.Tienda.Direccion,
                o.DatosEntregaCongelados ? o.RecogidaTelefono : o.Tienda.Telefono,
                o.NotaPedido,
                o.DatosEntregaCongelados ? o.DestinoNombre : o.UsuarioFinal.Nombre,
                o.Estado == EstadoOrden.Recolectada ? (o.DatosEntregaCongelados ? o.DestinoDireccion : o.UsuarioFinal.DireccionUbicacion) : "",
                o.Estado == EstadoOrden.Recolectada ? (o.DatosEntregaCongelados ? o.DestinoMapaLink : o.UsuarioFinal.MapaLink) : null,
                o.Estado == EstadoOrden.Recolectada ? (o.DatosEntregaCongelados ? o.DestinoTelefono : o.UsuarioFinal.Telefono) : "",
                o.Precio,
                o.DistanciaKm,
                o.DatosEntregaCongelados ? o.RecogidaLatitud : o.Tienda.Latitud,
                o.DatosEntregaCongelados ? o.RecogidaLongitud : o.Tienda.Longitud,
                o.CodigoA);
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
