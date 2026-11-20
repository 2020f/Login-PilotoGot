using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services
{
    public class TiendaAppService : ITiendaAppService
    {
        private readonly ApplicationDbContext _db;
        private readonly IOrdenService _ordenService;
        private readonly IUserContextService _userCtx;
        private readonly TimeProvider _clock;

        public TiendaAppService(
            ApplicationDbContext db,
            IOrdenService ordenService,
            IUserContextService userCtx, TimeProvider? clock = null)
        {
            _db = db;
            _ordenService = ordenService;
            _userCtx = userCtx;
            _clock = clock ?? TimeProvider.System;
        }

        // ---------- Tiendas ----------

        public async Task<List<TiendaInfoDto>> GetTiendasAsync(string identityUserId)
        {
            return await _db.UsuariosTienda
                .AsNoTracking()
                .Where(x => x.IdentityUserId == identityUserId && x.Activo && x.Tienda.Activo
                    && x.Tienda.ClienteApp.Estado == EstadoClienteApp.Activo)
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

            var coords = MapaLinkParser.Parse(mapaLink);

            var uf = new UsuarioFinal
            {
                TiendaId = tienda.Id,
                Nombre = nombre.Trim(),
                DireccionUbicacion = direccionUbicacion.Trim(),
                Telefono = telefono.Trim(),
                MapaLink = string.IsNullOrWhiteSpace(mapaLink) ? null : mapaLink.Trim(),
                Latitud = coords?.Lat,
                Longitud = coords?.Lng,
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

            var coords = MapaLinkParser.Parse(mapaLink);
            u.Latitud = coords?.Lat;
            u.Longitud = coords?.Lng;

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
                .AnyAsync(o => o.UsuarioFinalId == u.Id)
                || await _db.ParadasEntrega.AsNoTracking().AnyAsync(p => p.UsuarioFinalId == u.Id);

            if (tieneOrdenes)
                throw new InvalidOperationException("No puedes eliminar: este usuario final ya tiene órdenes.");

            _db.UsuariosFinales.Remove(u);
            await _db.SaveChangesAsync();
        }

        // ---------- Órdenes ----------

        public async Task<List<OrdenTiendaDto>> GetOrdenesAsync(string identityUserId)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);
            var query = OrdenesTienda(tienda);
            var recientes = query.Where(o => EstadosEntrega.Terminales.Contains(o.Estado))
                .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(50).Select(o => o.Id);
            // Compatibilidad con clientes que muestran activas e historial juntos:
            // nunca limitar trabajo abierto; sólo se acota la muestra histórica.
            return await query.Where(o => !EstadosEntrega.Terminales.Contains(o.Estado) || recientes.Contains(o.Id))
                .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Select(ProyectarOrden).ToListAsync();
        }

        public async Task<List<OrdenTiendaDto>> GetOrdenesActivasAsync(string identityUserId)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);
            return await OrdenesTienda(tienda).Where(o => !EstadosEntrega.Terminales.Contains(o.Estado))
                .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Select(ProyectarOrden).ToListAsync();
        }

        public async Task<PaginaOrdenesTiendaDto> GetHistorialOrdenesAsync(string identityUserId, int pagina = 1,
            int tamano = 30, string? buscar = null)
        {
            if (pagina < 1 || tamano is < 1 or > 100 || (long)(pagina - 1) * tamano > int.MaxValue)
                throw new ArgumentException("La página debe ser positiva y el tamaño debe estar entre 1 y 100.");
            buscar = buscar?.Trim();
            if (buscar?.Length > 120) throw new ArgumentException("La búsqueda admite hasta 120 caracteres.");
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);
            var query = OrdenesTienda(tienda).Where(o => EstadosEntrega.Terminales.Contains(o.Estado));
            if (!string.IsNullOrEmpty(buscar))
            {
                var numero = int.TryParse(buscar, out var parsed) ? parsed : -1;
                query = query.Where(o => o.NumeroOrdenA == numero || (o.CodigoA != null && o.CodigoA.Contains(buscar))
                    || (o.DatosEntregaCongelados ? o.DestinoNombre.Contains(buscar) || o.DestinoDireccion.Contains(buscar)
                        : o.UsuarioFinal != null && (o.UsuarioFinal.Nombre.Contains(buscar) || o.UsuarioFinal.DireccionUbicacion.Contains(buscar)))
                    || o.Paradas.Any(p => p.Nombre.Contains(buscar) || p.Direccion.Contains(buscar))
                    || _db.DatosEnviosOperativos.Any(d => d.ClienteAppId == tienda.ClienteAppId && d.OrdenEntregaId == o.Id
                        && d.Referencia != null && d.Referencia.Contains(buscar)));
            }
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
                .Skip((pagina - 1) * tamano).Take(tamano).Select(ProyectarOrden).ToListAsync();
            return new(items, pagina, tamano, total, (int)Math.Ceiling((double)total / tamano));
        }

        private IQueryable<OrdenEntrega> OrdenesTienda(Tienda tienda) => _db.OrdenesEntrega.AsNoTracking()
            .Where(o => o.TiendaId == tienda.Id && o.ClienteAppId == tienda.ClienteAppId);

        private static Expression<Func<OrdenEntrega, OrdenTiendaDto>> ProyectarOrden => o => new OrdenTiendaDto(
                    o.Id,
                    o.NumeroOrdenA,
                    o.Estado.ToString(),
                    (int)o.Estado,
                    o.DatosEntregaCongelados ? o.DestinoNombre : o.UsuarioFinal != null ? o.UsuarioFinal.Nombre : "",
                    o.CreatedAt,
                    o.AssignedAt,
                    o.RecolectadaAt,
                    o.EntregadaAt,
                    o.Precio,
                    o.DistanciaKm,
                    o.CodigoA) { FinalizadaAt = o.FinalizadaAt, Paradas = o.Paradas.OrderBy(p => p.Posicion).Select(p => new ParadaPilotoDto(
                        p.Id, p.UsuarioFinalId, p.Estado.ToString(),
                        p.EntregadaAt, p.Nombre, p.Direccion, p.Telefono, p.MapaLink, p.Latitud, p.Longitud, p.Instrucciones) { DevueltaAt = p.DevueltaAt }).ToList() };

        public async Task<OrdenQrDto> GetOrdenQrAsync(string identityUserId, int ordenId)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            var orden = await _db.OrdenesEntrega
                .AsNoTracking()
                .Include(o => o.UsuarioFinal)
                .Include(o => o.Codigos)
                .Include(o => o.Paradas)
                .FirstOrDefaultAsync(o => o.Id == ordenId && o.TiendaId == tienda.Id && o.ClienteAppId == tienda.ClienteAppId);

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
                orden.Paradas.Count > 1 ? null : codigoC,
                orden.DatosEntregaCongelados ? orden.DestinoNombre : orden.UsuarioFinal.Nombre,
                orden.DatosEntregaCongelados ? orden.DestinoDireccion : orden.UsuarioFinal.DireccionUbicacion,
                orden.DatosEntregaCongelados ? orden.DestinoMapaLink : orden.UsuarioFinal.MapaLink,
                orden.NotaPedido,
                orden.CodigoA) { Paradas = orden.Paradas.OrderBy(p => p.Posicion).Select(p => new ParadaCreadaDto(
                    p.Id, p.UsuarioFinalId, p.Nombre, orden.Codigos.Single(c => c.Tipo == TipoCodigo.C_Finalizacion
                        && c.ParadaEntregaId == p.Id).Codigo, p.Direccion)).ToList() };
        }

        public async Task<OrdenCreadaResult> CrearOrdenAsync(string identityUserId, int usuarioFinalId, string? notaPedido, string? operacionId = null, IReadOnlyList<OrdenDestinatarioRequest>? destinatarios = null)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);

            return await _ordenService.CrearOrdenAsync(
                clienteAppId: tienda.ClienteAppId,
                tiendaId: tienda.Id,
                usuarioFinalId: usuarioFinalId,
                notaPedido: notaPedido,
                actorIdentityUserId: identityUserId,
                actorRol: "Cliente",
                operacionId: operacionId, destinatarios: destinatarios);
        }

        public async Task<ResumenOrdenesTiendaDto> GetResumenOrdenesAsync(string identityUserId)
        {
            var tienda = await GetTiendaActivaEntityAsync(identityUserId);
            TimeZoneInfo zona;
            try { zona = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo"); }
            catch (TimeZoneNotFoundException) { zona = TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time"); }
            var hoy = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), zona).Date;
            var desde = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(hoy, DateTimeKind.Unspecified), zona);
            var hasta = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(hoy.AddDays(1), DateTimeKind.Unspecified), zona);
            return await _db.OrdenesEntrega.AsNoTracking()
                .Where(o => o.TiendaId == tienda.Id && o.ClienteAppId == tienda.ClienteAppId)
                .GroupBy(_ => 1)
                .Select(g => new ResumenOrdenesTiendaDto(
                    g.Count(o => o.Estado == EstadoOrden.Asignada),
                    g.Count(o => o.Estado == EstadoOrden.Recolectada),
                    g.Count(o => o.Estado == EstadoOrden.Entregada && o.EntregadaAt >= desde && o.EntregadaAt < hasta),
                    g.Count(o => o.Estado == EstadoOrden.Creada),
                    g.Count(o => o.Estado == EstadoOrden.Incidente),
                    g.Count(o => o.Estado == EstadoOrden.Creada || o.Estado == EstadoOrden.Asignada || o.Estado == EstadoOrden.Recolectada)))
                .SingleOrDefaultAsync() ?? new ResumenOrdenesTiendaDto(0, 0, 0, 0, 0, 0);
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
