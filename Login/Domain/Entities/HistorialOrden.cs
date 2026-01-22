using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;

namespace Login.Domain.Entities
{
    public class HistorialOrden
    {
        public int Id { get; set; }

        public int OrdenEntregaId { get; set; }
        public OrdenEntrega OrdenEntrega { get; set; } = null!;

        public TipoEventoOrden TipoEvento { get; set; }

        [MaxLength(450)]
        public string? ActorIdentityUserId { get; set; }

        [MaxLength(40)]
        public string ActorRol { get; set; } = "System";

        [Required, MaxLength(500)]
        public string Descripcion { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
