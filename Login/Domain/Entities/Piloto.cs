using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;

namespace Login.Domain.Entities
{
    public class Piloto
    {
        public int Id { get; set; }

        public int ClienteAppId { get; set; }
        public ClienteApp ClienteApp { get; set; } = null!;

        [Required, MaxLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = string.Empty;

        public EstadoPiloto Estado { get; set; } = EstadoPiloto.Disponible;

        // ✅ Relación con Identity (usuario que inicia sesión)
        [Required, MaxLength(450)]
        public string IdentityUserId { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<OrdenEntrega> Ordenes { get; set; } = new List<OrdenEntrega>();
    }
}
