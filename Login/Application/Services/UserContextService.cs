using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services
{
    public class UserContextService : IUserContextService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor? _http;
        public UserContextService(ApplicationDbContext db, IHttpContextAccessor? http = null)
        {
            _db = db;
            _http = http;
        }

        public async Task<int> GetTiendaActivaIdAsync(string identityUserId)
        {
            // Los clientes nuevos fijan la sucursal en cada petición. Cambiar
            // la preferencia de otro dispositivo no cambia este contexto.
            var request = _http?.HttpContext?.Request;
            var esSeleccion = request is not null && HttpMethods.IsPost(request.Method)
                && request.Path.Equals("/api/tienda/tienda-activa", StringComparison.OrdinalIgnoreCase);
            var esComprobante = request is not null &&
                (request.Path.Equals("/Tienda/Comprobante", StringComparison.OrdinalIgnoreCase)
                 || request.Path.Equals("/Qr/Orden", StringComparison.OrdinalIgnoreCase));
            Microsoft.Extensions.Primitives.StringValues header = default;
            var explicita = request is not null && !esSeleccion &&
                (request.Headers.TryGetValue("X-Tienda-Id", out header)
                 || (esComprobante && request.Query.TryGetValue("tiendaId", out header)));
            if (explicita)
            {
                if (header.Count != 1 || !int.TryParse(header[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var seleccionada) || seleccionada <= 0)
                    throw new System.InvalidOperationException("El contexto de tienda no es válido.");
                if (!await UsuarioTieneAccesoATiendaAsync(identityUserId, seleccionada))
                    throw new System.InvalidOperationException("La tienda seleccionada no está disponible o no tienes acceso.");
                return seleccionada;
            }

            var ctx = await _db.UserContexts.SingleOrDefaultAsync(x => x.IdentityUserId == identityUserId);
            if (ctx?.TiendaActivaId is int tiendaId)
            {
                if (!await UsuarioTieneAccesoATiendaAsync(identityUserId, tiendaId))
                    throw new System.InvalidOperationException("La tienda activa no está disponible. Selecciona otra tienda autorizada.");
                return tiendaId;
            }

            // si no hay tienda activa, intenta usar la primera tienda permitida
            var first = await _db.UsuariosTienda
                .AsNoTracking()
                .Where(x => x.IdentityUserId == identityUserId && x.Activo && x.Tienda.Activo
                    && x.Tienda.ClienteApp.Estado == EstadoClienteApp.Activo)
                .OrderBy(x => x.TiendaId)
                .Select(x => x.TiendaId)
                .FirstOrDefaultAsync();

            if (first == 0)
                throw new System.InvalidOperationException("Este usuario no tiene tiendas asignadas.");

            // Un default determinista no necesita escritura. Evita una carrera
            // del índice único cuando varias peticiones abren la cuenta a la vez.
            return first;
        }

        public async Task SetTiendaActivaIdAsync(string identityUserId, int tiendaId)
        {
            var tenantId = await _db.Tiendas.AsNoTracking().Where(t => t.Id == tiendaId)
                .Select(t => t.ClienteAppId).SingleOrDefaultAsync();
            if (tenantId == 0) throw new System.InvalidOperationException("No tienes acceso a esa tienda.");
            // Serializar la preferencia de cuentas antiguas evita la carrera
            // de dos primeras selecciones sobre el índice único por usuario.
            // Tenant -> usuario conserva el mismo orden de bloqueo que las
            // altas y revocaciones administrativas de pertenencias.
            using var tx = await _db.Database.BeginTransactionAsync();
            var disponible = await _db.ClientesApp.Where(c => c.Id == tenantId && c.Estado == EstadoClienteApp.Activo)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrdenSeq, c => c.OrdenSeq));
            if (disponible != 1) throw new System.InvalidOperationException("La tienda no está disponible.");
            var bloqueado = await _db.Users.Where(u => u.Id == identityUserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount));
            if (bloqueado != 1) throw new System.InvalidOperationException("Usuario no disponible.");
            var tiene = await UsuarioTieneAccesoATiendaAsync(identityUserId, tiendaId);
            if (!tiene) throw new System.InvalidOperationException("No tienes acceso a esa tienda.");

            var ctx = await _db.UserContexts.SingleOrDefaultAsync(x => x.IdentityUserId == identityUserId);
            if (ctx is null)
                _db.UserContexts.Add(new Domain.Entities.UserContext { IdentityUserId = identityUserId, TiendaActivaId = tiendaId });
            else
                ctx.TiendaActivaId = tiendaId;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public Task<bool> UsuarioTieneAccesoATiendaAsync(string identityUserId, int tiendaId)
        {
            return _db.UsuariosTienda.AnyAsync(x =>
                x.IdentityUserId == identityUserId &&
                x.TiendaId == tiendaId &&
                x.Activo && x.Tienda.Activo && x.Tienda.ClienteApp.Estado == EstadoClienteApp.Activo);
        }
    }
}
