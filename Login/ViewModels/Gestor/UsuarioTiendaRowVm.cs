namespace Login.ViewModels.Gestor
{
    public class UsuarioTiendaRowVm
    {
        public int Id { get; set; }
        public string EmailUsuario { get; set; } = "";
        public int TiendaId { get; set; }
        public string TiendaNombre { get; set; } = "";
        public bool Activo { get; set; }
    }
}
