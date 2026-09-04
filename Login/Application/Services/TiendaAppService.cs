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
    public class TiendaAppService : ITiendaAppService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOrdenService _ordenService;
        private readonly IUserContextService _userCtx;

        public TiendaAppService(
            ApplicationDbContext db,
            IOrdenService ordenService,
            IUserContextService userCtx)
        {
            _db = db;
            _ordenService = ordenService;
            _userCtx = userCtx;
        }

        // ---------- Tiendas ----------

        public async Task<List<TiendaInfoDto>> GetTiendasAsync(string identityUserId)
        {
            return await _db.UsuariosTienda
                .AsNoTracking()
                .Where(x => x.IdentityUserId == identityUserId && x.Activo && x.Tienda.Activo)
                .OrderBy(x => x.Tienda.Nombre)
                .Select(x => new TiendaInfoDto(x.Tienda.Id, x.Tienda.Nombre, x.Tienda.Direccion, x.Tienda.Activo))
                .ToListAsync();
        }

        public async Task<TiendaActivaDto> GetTiendaActivaAsync(string identityUserId)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);
            return new TiendaActivaDto(tienda.Id, tienda.Nombre, tienda.Direccion, tienda.Telefono, tienda.ClienteAppId);
        }

        public async Task SetTiendaActivaAsync(string identityUserId, int tiendaId)
        {
            await _userCtx.SetTiendaActivaIdAsync(identityUserId, tiendaId);
        }

        // ---------- Usuarios finales ----------

        public async Task<List<UsuarioFinalDto>> GetUsuariosFinalesAsync(string identityUserId)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            return await _db.UsuariosFinales.AsNoTracking()
                .Where(u => u.TiendaId == tienda.Id)
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new UsuarioFinalDto(u.Id, u.Nombre, u.DireccionUbicacion, u.MapaLink, u.Telefono, u.Notas, u.Activo, u.CreatedAt))
                .ToListAsync();
        }

        public async Task<UsuarioFinalDto> GetUsuarioFinalAsync(string identityUserId, int id)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            var u = await _db.UsuariosFinales.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.TiendaId == tienda.Id);

            if (u is null)
                throw new InvalidOperationException("Usuario final no encontrado o no es de tu tienda.");

            return new UsuarioFinalDto(u.Id, u.Nombre, u.DireccionUbicacion, u.MapaLink, u.Telefono, u.Notas, u.Activo, u.CreatedAt);
        }

        public async Task<UsuarioFinalDto> CrearUsuarioFinalAsync(string identityUserId, string nombre, string direccionUbicacion, string telefono, string? notas, string? mapaLink)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            var uf = new UsuarioFinal
            {
                TiendaId = tienda.Id,
                Nombre = nombre.Trim(),
                DireccionUbicacion = direccionUbicacion.Trim(),
                Telefono = telefono.Trim(),
                MapaLink = string.IsNullOrWhiteSpace(mapaLink) ? null : mapaLink.Trim(),
                Notas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(),
                Activo = true
            };

            _db.UsuariosFinales.Add(uf);
            await _db.SaveChangesAsync();

            return new UsuarioFinalDto(uf.Id, uf.Nombre, uf.DireccionUbicacion, uf.MapaLink, uf.Telefono, uf.Notas, uf.Activo, uf.CreatedAt);
        }

        public async Task<UsuarioFinalDto> EditarUsuarioFinalAsync(string identityUserId, int id, string nombre, string direccionUbicacion, string telefono, string? notas, string? mapaLink, bool activo)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            var u = await _db.UsuariosFinales
                .SingleOrDefaultAsync(x => x.Id == id && x.TiendaId == tienda.Id);

            if (u is null)
                throw new InvalidOperationException("Usuario final no encontrado o no es de tu tienda.");

            u.Nombre = nombre.Trim();
            u.DireccionUbicacion = direccionUbicacion.Trim();
            u.Telefono = telefono.Trim();
            u.MapaLink = string.IsNullOrWhiteSpace(mapaLink) ? null : mapaLink.Trim();
            u.Notas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim();
            u.Activo = activo;

            await _db.SaveChangesAsync();

            return new UsuarioFinalDto(u.Id, u.Nombre, u.DireccionUbicacion, u.MapaLink, u.Telefono, u.Notas, u.Activo, u.CreatedAt);
        }

        public async Task EliminarUsuarioFinalAsync(string identityUserId, int id)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            var u = await _db.UsuariosFinales
                .SingleOrDefaultAsync(x => x.Id == id && x.TiendaId == tienda.Id);

            if (u is null)
                throw new InvalidOperationException("Usuario final no encontrado o no es de tu tienda.");

            var tieneOrdenes = await _db.OrdenesEntrega
                .AsNoTracking()
                .AnyAsync(o => o.UsuarioFinalId == u.Id);

            if (tieneOrdenes)
                throw new InvalidOperationException("No puedes eliminar: este usuario final ya tiene órdenes.");

            _db.UsuariosFinales.Remove(u);
            await _db.SaveChangesAsync();
        }

        // ---------- Órdenes ----------

        public async Task<List<OrdenTiendaDto>> GetOrdenesAsync(string identityUserId)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            return await _db.OrdenesEntrega.AsNoTracking()
                .Include(o => o.UsuarioFinal)
                .Where(o => o.TiendaId == tienda.Id)
                .OrderByDescending(o => o.CreatedAt)
                .Take(200)
                .Select(o => new OrdenTiendaDto(
                    o.Id,
                    o.NumeroOrdenA,
                    o.Estado.ToString(),
                    (int)o.Estado,
                    o.UsuarioFinal.Nombre,
                    o.CreatedAt,
                    o.AssignedAt,
                    o.RecolectadaAt,
                    o.EntregadaAt))
                .ToListAsync();
        }

        public async Task<OrdenQrDto> GetOrdenQrAsync(string identityUserId, int ordenId)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            var orden = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.UsuarioFinal)
                .Include(o => o.Codigos)
                .FirstOrDefaultAsync(o => o.Id == ordenId && o.TiendaId == tienda.Id);

            if (orden is null)
                throw new InvalidOperationException("Orden no encontrada o no es de tu tienda.");

            var codigoB = orden.Codigos
                .Where(x => x.Tipo == TipoCodigo.B_Recoleccion)
                .Select(x => x.Codigo)
                .FirstOrDefault() ?? "";

            var codigoC = orden.Codigos
                .Where(x => x.Tipo == TipoCodigo.C_Finalizacion)
                .Select(x => x.Codigo)
                .FirstOrDefault() ?? "";

            return new OrdenQrDto(
                orden.Id,
                orden.NumeroOrdenA,
                orden.Estado.ToString(),
                codigoB,
                codigoC,
                orden.UsuarioFinal.Nombre,
                orden.UsuarioFinal.DireccionUbicacion,
                orden.UsuarioFinal.MapaLink,
                orden.NotaPedido);
        }

        public async Task<OrdenCreadaResult> CrearOrdenAsync(string identityUserId, int usuarioFinalId, string? notaPedido)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            return await _ordenService.CrearOrdenAsync(
                clienteAppId: tienda.ClienteAppId,
                tiendaId: tienda.Id,
                usuarioFinalId: usuarioFinalId,
                notaPedido: notaPedido,
                actorIdentityUserId: identityUserId,
                actorRol: "Cliente");
        }

        // ---------- Helpers ----------

        private async Task<Tienda> GetTiendaActivaEntityAsync(string identityUserId)
        {
            if (string.IsNullOrWhiteSpace(identityUserId))
                throw new InvalidOperationException("Usuario no autenticado.");

            var tiendaId = await _userCtx.GetTiendaActivaIdAsync(identityUserId);

            var tieneAcceso = await _userCtx.UsuarioTieneAccesoATiendaAsync(identityUserId, tiendaId);
            if (!tieneAcceso)
                throw new InvalidOperationException("No tienes acceso a la tienda activa.");

            var tienda = await _db.Tiendas.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == tiendaId && t.Activo);

            if (tienda is null)
                throw new InvalidOperationException("Tienda activa inválida o desactivada.");

            return tienda;
        }
    }
}
