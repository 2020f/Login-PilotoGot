using System;
using System.Threading.Tasks;

namespace Login.Application.Interfaces
{
    /// <summary>
    /// Servicio de aplicación para la interfaz "Piloto" (rol Piloto).
    /// Usado por la app web y por la API móvil.
    /// </summary>
    public interface IPilotoAppService
    {
        /// <summary>Devuelve la orden activa del piloto (Asignada o Recolectada), o null.</summary>
        Task<OrdenPilotoDto?> GetOrdenActualAsync(string identityUserId);

        /// <summary>Confirma la recolecta con el código B (valida que la orden sea del piloto).</summary>
        Task ConfirmarRecolectaAsync(string identityUserId, string codigoB);

        /// <summary>Confirma la entrega con los códigos B + C (valida que la orden sea del piloto).</summary>
        Task ConfirmarEntregaAsync(string identityUserId, string codigoB, string codigoC);
    }

    public sealed record OrdenPilotoDto(
        int OrdenId,
        int NumeroOrdenA,
        string Estado,
        DateTime CreatedAt,
        bool PuedeVerDestinoFinal,
        string TiendaNombre,
        string TiendaDireccion,
        string TiendaTelefono,
        string? NotaPedido,
        string UsuarioFinalNombre,
        string UsuarioFinalDireccion,
        string UsuarioFinalTelefono);
}
