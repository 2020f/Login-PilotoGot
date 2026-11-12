using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.SuperAdmin
{
    public class GestorCreateVm
    {
        [Required]
        public int ClienteAppId { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, MinLength(12), MaxLength(128), DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }
}
