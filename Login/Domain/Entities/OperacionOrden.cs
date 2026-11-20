using System.ComponentModel.DataAnnotations;

namespace Login.Domain.Entities;

/// <summary>Resultado permanente de un comando de creación confirmado.</summary>
public sealed class OperacionOrden
{
    public int Id { get; set; }
    public int ClienteAppId { get; set; }
    public ClienteApp ClienteApp { get; set; } = null!;
    [MaxLength(450)] public string ActorIdentityUserId { get; set; } = "";
    [MaxLength(30)] public string Tipo { get; set; } = "CrearOrden";
    [MaxLength(64)] public string Clave { get; set; } = "";
    [MaxLength(64)] public string PayloadHash { get; set; } = "";
    public string? ResultadoJson { get; set; }
    public int OrdenEntregaId { get; set; }
    public OrdenEntrega OrdenEntrega { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
