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
        public int MaxTiendas { get; set; }
        public int MaxPilotos { get; set; }


        public int TiendasCount { get; set; }
        public int PilotosCount { get; set; }

        public string? Email { get; set; }
        public DateTime? FechaFinPlan { get; set; }
        public DateTime? ProximoCobro { get; set; }
        public bool? EsTrial { get; set; }

    }
}
