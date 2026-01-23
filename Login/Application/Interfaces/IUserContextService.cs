using System.Threading.Tasks;
using Login.Domain.Entities;

namespace Login.Application.Interfaces
{
    public interface IUserContextService
    {
        Task<int> GetTiendaActivaIdAsync(string identityUserId);
        Task SetTiendaActivaIdAsync(string identityUserId, int tiendaId);
        Task<bool> UsuarioTieneAccesoATiendaAsync(string identityUserId, int tiendaId);
    }
}
