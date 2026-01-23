using System.Collections.Generic;

namespace Login.ViewModels.Tienda
{
    public class UsuariosFinalesIndexVm
    {
        public int TiendaId { get; set; }
        public string? Ok { get; set; }
        public string? Error { get; set; }
        public List<UsuarioFinalRowVm> Usuarios { get; set; } = new();
    }
}
