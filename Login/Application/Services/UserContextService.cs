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
        public UserContextService(ApplicationDbContext db) => _db = db;

        public async Task<int> GetTiendaActivaIdAsync(string identityUserId)
        {
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
            var tiene = await UsuarioTieneAccesoATiendaAsync(identityUserId, tiendaId);
            if (!tiene) throw new System.InvalidOperationException("No tienes acceso a esa tienda.");

            var ctx = await _db.UserContexts.SingleOrDefaultAsync(x => x.IdentityUserId == identityUserId);
            if (ctx is null)
                _db.UserContexts.Add(new Domain.Entities.UserContext { IdentityUserId = identityUserId, TiendaActivaId = tiendaId });
            else
                ctx.TiendaActivaId = tiendaId;

            await _db.SaveChangesAsync();
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
