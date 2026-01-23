namespace Login.ViewModels.SuperAdmin
{
    public class ClienteAppRowVm
    {
        public int Id { get; set; }
        public string NombreComercial { get; set; } = "";
        public string PlanNombre { get; set; } = "";
        public bool Activo { get; set; }
        public string Estado { get; set; } = "";
        public string? GestorEmail { get; set; }
        public int TiendasCount { get; set; }
        public int PilotosCount { get; set; }
    }
}
