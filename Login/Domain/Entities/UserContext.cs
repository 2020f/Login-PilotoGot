using System.ComponentModel.DataAnnotations;

namespace Login.Domain.Entities
{
    public class UserContext
    {
        public int Id { get; set; }

        [Required, MaxLength(450)]
        public string IdentityUserId { get; set; } = string.Empty;

        public int? TiendaActivaId { get; set; }
    }
}
