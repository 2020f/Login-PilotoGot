using System;
using System.Threading.Tasks;

namespace Login.Application.Interfaces
{
    public interface IOrdenService
    {
        /// <summary>
        /// Crea una orden: genera A (secuencial por ClienteApp), y crea 2 códigos:
        /// B (recolecta) y C (entrega).
        /// </summary>
        Task<OrdenCreadaResult> CrearOrdenAsync(
            int clienteAppId,
            int tiendaId,
            int usuarioFinalId,
            string? notaPedido = null,
            string actorIdentityUserId = "System",
            string actorRol = "System");

        /// <summary>Asigna piloto a una orden (por código B).</summary>
        Task AsignarPilotoAsync(
            string codigoB,
            int pilotoId,
            string actorIdentityUserId,
            string actorRol);

        /// <summary>Confirma recolecta usando B (marca B como usado y cambia estado).</summary>
        Task ConfirmarRecolectaAsync(
            string codigoB,
            string actorIdentityUserId,
            string actorRol);

        /// <summary>Confirma entrega usando B + C (marca C como usado y cambia estado).</summary>
        Task ConfirmarEntregaAsync(
            string codigoB,
            string codigoC,
            string actorIdentityUserId,
            string actorRol);

        /// <summary>Valida límite mensual de órdenes según el plan del ClienteApp.</summary>
        Task ValidarLimitePorPlanAsync(int clienteAppId, DateTime fechaUtc);

        /// <summary>
        /// Devuelve info “segura” para piloto. No revela datos sensibles antes de recolecta.
        /// </summary>
        Task<OrdenPilotoView> ObtenerOrdenParaPilotoAsync(string codigoB, int pilotoId);

        /// <summary>Lista de órdenes en modo auto disponibles para que un piloto las acepte.</summary>
        Task<List<OrdenAutoDisponibleDto>> GetOrdenesAutoDisponiblesAsync(int pilotoId);

        /// <summary>Un piloto acepta una orden auto (control de concurrencia: el primero se la queda).</summary>
        Task AceptarOrdenAutoAsync(int ordenId, int pilotoId, string actorIdentityUserId, string actorRol);

        /// <summary>Un piloto rechaza una orden auto (queda fuera de su lista).</summary>
        Task RechazarOrdenAutoAsync(int ordenId, int pilotoId);
    }

    public sealed record OrdenCreadaResult(
        int OrdenId,
        int NumeroOrdenA,
        string CodigoB,
        string CodigoC
    );

    public sealed record OrdenAutoDisponibleDto(
        int OrdenId,
        int NumeroOrdenA,
        string TiendaNombre,
        string TiendaDireccion,
        string UsuarioFinalNombre,
        string? NotaPedido,
        DateTime CreatedAt,
        double? DistanciaKm
    );

    public sealed record OrdenPilotoView(
        int OrdenId,
        int NumeroOrdenA,
        string Estado,
        bool PuedeVerDestino,
        string? DestinoDireccion,
        string? NombreUsuarioFinal,
        string? TelefonoUsuarioFinal
    );
}
