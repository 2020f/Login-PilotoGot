using System;
using System.Collections.Generic;
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

        /// <summary>Devuelve todas las órdenes activas del piloto (Asignada + Recolectada).</summary>
        Task<List<OrdenPilotoDto>> GetOrdenesAsync(string identityUserId);

        /// <summary>Devuelve el detalle de una orden específica (valida propiedad).</summary>
        Task<OrdenPilotoDto?> GetOrdenDetalleAsync(string identityUserId, int ordenId);
        Task<PaginaOrdenesPilotoDto> GetHistorialAsync(string identityUserId, int pagina = 1, int tamano = 30);
        Task<OrdenPilotoDto?> GetOrdenHistoricaAsync(string identityUserId, int ordenId);

        /// <summary>Confirma la recolecta con el código B (valida que la orden sea del piloto).</summary>
        Task ConfirmarRecolectaAsync(string identityUserId, string codigoB, int? ordenId = null, string? operacionId = null);
        Task<EntregaParadaResult> ConfirmarEntregaParadaAsync(string identityUserId, int ordenId, int paradaId,
            string codigoC, string? operacionId = null);

        /// <summary>Confirma la entrega con los códigos B + C (valida que la orden sea del piloto).</summary>
        Task ConfirmarEntregaAsync(string identityUserId, string codigoB, string codigoC, int? ordenId = null);

        /// <summary>Órdenes en modo auto disponibles para que este piloto las acepte.</summary>
        Task<List<OrdenAutoDisponibleDto>> GetOrdenesAutoAsync(string identityUserId);

        /// <summary>Acepta una orden auto (control de concurrencia).</summary>
        Task AceptarOrdenAutoAsync(string identityUserId, int ordenId);

        /// <summary>Rechaza una orden auto.</summary>
        Task RechazarOrdenAutoAsync(string identityUserId, int ordenId);

        /// <summary>Actualiza la ubicación GPS del piloto (reportada por la app móvil).</summary>
        Task ActualizarUbicacionAsync(string identityUserId, double latitud, double longitud);

        /// <summary>Devuelve el estado en línea/ocupado del piloto.</summary>
        Task<PilotoEstadoDto> GetEstadoAsync(string identityUserId);

        /// <summary>Activa/desactiva el estado "en línea" del piloto.</summary>
        Task SetEnLineaAsync(string identityUserId, bool enLinea);
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
        string? UsuarioFinalMapaLink,
        string UsuarioFinalTelefono,
        decimal? Precio,
        double? DistanciaKm,
        double? TiendaLatitud,
        double? TiendaLongitud,
        string? CodigoA = null)
    {
        public IReadOnlyList<ParadaPilotoDto> Paradas { get; init; } = Array.Empty<ParadaPilotoDto>();
        public int TotalParadas => Paradas.Count;
        public int Entregadas => Paradas.Count(p => p.Estado == "Entregada");
        public int Devueltas => Paradas.Count(p => p.Estado == "Devuelta");
        public DateTime? FinalizadaAt { get; init; }
        public bool PuedeRecoger { get; init; }
        public string? BloqueoRecogida { get; init; }
    }

    public sealed record ParadaPilotoDto(int ParadaId, int UsuarioFinalId, string Estado, DateTime? EntregadaAt,
        string Nombre, string? Direccion, string? Telefono, string? MapaLink, double? Latitud,
        double? Longitud, string? Instrucciones)
    {
        public DateTime? DevueltaAt { get; init; }
        public bool PuedeConfirmarEntrega { get; init; }
        public string? BloqueoEntrega { get; init; }
    }

    public sealed record PilotoEstadoDto(
        bool EnLinea,
        string Estado);
    public sealed record PaginaOrdenesPilotoDto(IReadOnlyList<OrdenPilotoDto> Items, int Pagina, int Total, int TotalPaginas);
}
