using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Login.Application.Interfaces
{
    /// <summary>
    /// Servicio de aplicación para la interfaz "Tienda" (rol Cliente).
    /// Usado tanto por la app web como por la API móvil.
    /// Toda la lógica queda aislada por tenant (ClienteApp) y por tienda activa.
    /// </summary>
    public interface ITiendaAppService
    {
        // Tiendas
        Task<List<TiendaInfoDto>> GetTiendasAsync(string identityUserId);
        Task<TiendaActivaDto> GetTiendaActivaAsync(string identityUserId);
        Task SetTiendaActivaAsync(string identityUserId, int tiendaId);

        // Usuarios finales (destinatarios)
        Task<List<UsuarioFinalDto>> GetUsuariosFinalesAsync(string identityUserId);
        Task<UsuarioFinalDto> GetUsuarioFinalAsync(string identityUserId, int id);
        Task<UsuarioFinalDto> CrearUsuarioFinalAsync(string identityUserId, string nombre, string direccionUbicacion, string telefono, string? notas, string? mapaLink);
        Task<UsuarioFinalDto> EditarUsuarioFinalAsync(string identityUserId, int id, string nombre, string direccionUbicacion, string telefono, string? notas, string? mapaLink, bool activo);
        Task EliminarUsuarioFinalAsync(string identityUserId, int id);

        // Órdenes
        Task<List<OrdenTiendaDto>> GetOrdenesAsync(string identityUserId);
        Task<OrdenQrDto> GetOrdenQrAsync(string identityUserId, int ordenId);
        Task<OrdenCreadaResult> CrearOrdenAsync(string identityUserId, int usuarioFinalId, string? notaPedido);
    }

    public sealed record TiendaInfoDto(int Id, string Nombre, string Direccion, bool Activo);
    public sealed record TiendaActivaDto(int Id, string Nombre, string Direccion, string Telefono, int ClienteAppId);

    public sealed record UsuarioFinalDto(
        int Id,
        string Nombre,
        string DireccionUbicacion,
        string? MapaLink,
        string Telefono,
        string? Notas,
        bool Activo,
        DateTime CreatedAt);

    public sealed record OrdenTiendaDto(
        int OrdenId,
        int NumeroOrdenA,
        string Estado,
        int EstadoNum,
        string UsuarioFinal,
        DateTime CreatedAt,
        DateTime? AssignedAt,
        DateTime? RecolectadaAt,
        DateTime? EntregadaAt);

    public sealed record OrdenQrDto(
        int OrdenId,
        int NumeroOrdenA,
        string Estado,
        string CodigoB,
        string CodigoC,
        string UsuarioFinalNombre,
        string DireccionUbicacion,
        string? MapaLink,
        string? Descripcion);
}
