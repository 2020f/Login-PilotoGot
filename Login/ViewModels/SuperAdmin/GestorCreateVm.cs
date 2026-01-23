using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.SuperAdmin
{
    public class GestorCreateVm
    {
        [Required]
        public int ClienteAppId { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, MinLength(6)]
        public string Password { get; set; } = "Admin123";
    }
}
