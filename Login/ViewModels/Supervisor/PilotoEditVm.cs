using System.ComponentModel.DataAnnotations;
using Login.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Login.ViewModels.Supervisor
{
    public class PilotoEditVm
    {
        [Required]
        public int Id { get; set; }

        [Required, MaxLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = string.Empty;

        public EstadoPiloto Estado { get; set; } = EstadoPiloto.Disponible;

        public bool Activo { get; set; } = true;

        private string _emailPiloto = string.Empty;
        [Required(ErrorMessage = "Escribe el correo de acceso.")]
        [EmailAddress(ErrorMessage = "Escribe un correo válido.")]
        [MaxLength(256)]
        public string EmailPiloto
        {
            get => _emailPiloto;
            set => _emailPiloto = value?.Trim() ?? string.Empty;
        }

        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Usa entre 6 y 100 caracteres para la nueva contraseña.")]
        public string? NuevoPassword { get; set; }

        [DataType(DataType.Password)]
        [Compare(nameof(NuevoPassword), ErrorMessage = "La confirmación no coincide con la nueva contraseña.")]
        public string? ConfirmarPassword { get; set; }

        [BindNever]
        public bool PuedeEditarAcceso { get; set; }

        [BindNever]
        public string? MotivoAccesoBloqueado { get; set; }
    }
}
