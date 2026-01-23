using System.ComponentModel.DataAnnotations;

namespace Login.Domain.Entities
{
    public class UsuarioTienda
    {
        public int Id { get; set; }

        [Required, MaxLength(450)]
        public string IdentityUserId { get; set; } = string.Empty;

        public int TiendaId { get; set; }
        public Tienda Tienda { get; set; } = null!;

        public bool Activo { get; set; } = true;

        // Opcional: si quieres marcar “encargado/manager”
        public bool EsPrincipal { get; set; } = false;
    }
}
