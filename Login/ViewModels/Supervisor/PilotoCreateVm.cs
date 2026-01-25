using System.ComponentModel.DataAnnotations;

namespace Login.ViewModels.Supervisor
{
    public class PilotoCreateVm
    {
        [Required, MaxLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        public string EmailPiloto { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string PasswordPiloto { get; set; } = string.Empty;
    }
}
