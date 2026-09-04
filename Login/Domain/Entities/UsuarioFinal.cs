using System.ComponentModel.DataAnnotations;

namespace Login.Domain.Entities
{
    public class UsuarioFinal
    {
        public int Id { get; set; }

        public int TiendaId { get; set; }
        public Tienda Tienda { get; set; } = null!;

        [Required, MaxLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MaxLength(250)]
        public string DireccionUbicacion { get; set; } = string.Empty;

        // Enlace exacto de Google Maps (ej. https://maps.google.com/maps?q=18.4812053,-69.9743033&z=17&hl=es)
        [MaxLength(500)]
        public string? MapaLink { get; set; }

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = string.Empty;

        [MaxLength(400)]
        public string? Notas { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<OrdenEntrega> Ordenes { get; set; } = new List<OrdenEntrega>();
    }
}
