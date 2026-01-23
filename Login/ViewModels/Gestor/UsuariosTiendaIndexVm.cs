using System.Collections.Generic;

namespace Login.ViewModels.Gestor
{
    public class UsuariosTiendaIndexVm
    {
        public string? Ok { get; set; }
        public string? Error { get; set; }
        public List<UsuarioTiendaRowVm> Asignaciones { get; set; } = new();
    }
}
