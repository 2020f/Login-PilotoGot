using System.ComponentModel.DataAnnotations;
using Login.Domain.Entities;
using Login.ViewModels.Tienda;
using Microsoft.AspNetCore.Identity;


namespace Login.ViewModels.Tienda
{
    public class TiendaCreateVm
    {
        [Required, MaxLength(120)]
        public string Nombre { get; set; } = "";

        [Required, MaxLength(200)]
        public string Direccion { get; set; } = "";

        [Required, MaxLength(30)]
        public string Telefono { get; set; } = "";

        [Required, EmailAddress, MaxLength(256)]
        public string EmailCliente { get; set; } = "";

        [Required, MinLength(6)]
        public string PasswordCliente { get; set; } = "";
    }
}
