using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;

namespace Login.Domain.Entities
{
    public class CodigoEntrega
    {
        public int Id { get; set; }

        public int OrdenEntregaId { get; set; }
        public OrdenEntrega OrdenEntrega { get; set; } = null!;

        public TipoCodigo Tipo { get; set; }

        [Required, MaxLength(80)]
        public string Codigo { get; set; } = string.Empty;

        public bool Usado { get; set; } = false;
        public DateTime? UsadoAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
