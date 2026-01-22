using System.ComponentModel.DataAnnotations;

namespace Login.Domain.Entities
{
    public class Tienda
    {
        public int Id { get; set; }

        public int ClienteAppId { get; set; }
        public ClienteApp ClienteApp { get; set; } = null!;

        [Required, MaxLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Direccion { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<UsuarioFinal> UsuariosFinales { get; set; } = new List<UsuarioFinal>();
        public ICollection<OrdenEntrega> Ordenes { get; set; } = new List<OrdenEntrega>();
    }
}
