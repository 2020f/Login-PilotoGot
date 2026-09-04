using System;

namespace Login.ViewModels.Tienda
{
    public class UsuarioFinalRowVm
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string DireccionUbicacion { get; set; } = "";
        public string? MapaLink { get; set; }
        public string Telefono { get; set; } = "";
        public bool Activo { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
