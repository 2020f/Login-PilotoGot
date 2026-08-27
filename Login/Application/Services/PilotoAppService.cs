using System;
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

            return await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.Tienda)
                .Include(o => o.UsuarioFinal)
                .Where(o => o.PilotoId == piloto.Id &&
                            (o.Estado == EstadoOrden.Asignada || o.Estado == EstadoOrden.Recolectada))
                .OrderByDescending(o => o.AssignedAt ?? o.CreatedAt)
                .Select(o => new OrdenPilotoDto(
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
                    o.UsuarioFinal.Telefono))
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

        private async Task<Piloto?> GetPilotoLogueadoAsync(string identityUserId)
        {
            if (string.IsNullOrWhiteSpace(identityUserId))
                throw new InvalidOperationException("Usuario no autenticado.");

            return await _db.Pilotos.AsNoTracking()
                .SingleOrDefaultAsync(p => p.IdentityUserId == identityUserId && p.Activo);
        }
    }
}
