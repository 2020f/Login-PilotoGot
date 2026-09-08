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

        public PilotoAppService(ApplicationDbContext db, IOrdenService ordenService)
        {
            _db = db;
            _ordenService = ordenService;
        }

        public async Task<OrdenPilotoDto?> GetOrdenActualAsync(string identityUserId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return null;

            return await OrdenesActivasQuery(piloto.Id)
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt)
                .Select(ToDto())
                .FirstOrDefaultAsync();
        }

        public async Task<List<OrdenPilotoDto>> GetOrdenesAsync(string identityUserId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return new List<OrdenPilotoDto>();

            return await OrdenesActivasQuery(piloto.Id)
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt)
                .Select(ToDto())
                .ToListAsync();
        }

        public async Task<OrdenPilotoDto?> GetOrdenDetalleAsync(string identityUserId, int ordenId)
        {
            var piloto = await GetPilotoLogueadoAsync(identityUserId);
            if (piloto is null) return null;

            return await OrdenesActivasQuery(piloto.Id)
                .Where(o => o.Id == ordenId)
                .Select(ToDto())
                .FirstOrDefaultAsync();
        }

        public async Task ConfirmarRecolectaAsync(string identityUserId, string codigoB)
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
                    o.Estado == EstadoOrden.Asignada &&
                    o.Codigos.Any(c => c.Tipo == TipoCodigo.B_Recoleccion && c.Codigo == codigoB));

            if (!ordenValida)
                throw new InvalidOperationException("Ese B no corresponde a una orden asignada a ti.");

            await _ordenService.ConfirmarRecolectaAsync(codigoB, identityUserId, "Piloto");
        }

        public async Task ConfirmarEntregaAsync(string identityUserId, string codigoB, string codigoC)
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
                    o.Estado == EstadoOrden.Recolectada &&
                    o.Codigos.Any(c => c.Tipo == TipoCodigo.B_Recoleccion && c.Codigo == codigoB));

            if (!ordenValida)
                throw new InvalidOperationException("No puedes cerrar: la orden no está recolectada o no es tuya.");

            await _ordenService.ConfirmarEntregaAsync(codigoB, codigoC, identityUserId, "Piloto");
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

            return new PilotoEstadoDto(piloto.EnLinea, piloto.Estado.ToString());
        }

        public async Task SetEnLineaAsync(string identityUserId, bool enLinea)
        {
            var piloto = await _db.Pilotos
                .SingleOrDefaultAsync(p => p.IdentityUserId == identityUserId && p.Activo);

            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            piloto.EnLinea = enLinea;

            // Si se pone en línea y estaba Inactivo por error, lo dejamos Disponible
            if (enLinea && piloto.Estado == EstadoPiloto.Inactivo)
                piloto.Estado = EstadoPiloto.Disponible;

            await _db.SaveChangesAsync();
        }

        // ---------- Helpers ----------

        private IQueryable<OrdenEntrega> OrdenesActivasQuery(int pilotoId)        {
            return _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Tienda)
                .Include(o => o.UsuarioFinal)
                .Where(o => o.PilotoId == pilotoId &&
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
                o.Tienda.Nombre,
                o.Tienda.Direccion,
                o.Tienda.Telefono,
                o.NotaPedido,
                o.UsuarioFinal.Nombre,
                o.UsuarioFinal.DireccionUbicacion,
                o.UsuarioFinal.MapaLink,
                o.UsuarioFinal.Telefono,
                o.Precio,
                o.DistanciaKm,
                o.Tienda.Latitud,
                o.Tienda.Longitud);
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
