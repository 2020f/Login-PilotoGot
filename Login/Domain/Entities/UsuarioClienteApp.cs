using System.ComponentModel.DataAnnotations;

namespace Login.Domain.Entities
{
    public class UsuarioClienteApp
    {
        public int Id { get; set; }

        [Required, MaxLength(450)]
        public string IdentityUserId { get; set; } = string.Empty;

        public int ClienteAppId { get; set; }
        public ClienteApp ClienteApp { get; set; } = null!;

        public bool Activo { get; set; } = true;

        // “Gestor principal” del tenant
        public bool EsPrincipal { get; set; } = false;
    }
}
