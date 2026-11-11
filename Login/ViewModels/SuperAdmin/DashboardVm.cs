namespace Login.ViewModels.SuperAdmin;

public sealed class DashboardVm
{
    public int Empresas { get; set; }
    public int EmpresasActivas { get; set; }
    public int EmpresasBloqueadas { get; set; }
    public int ComerciosActivos { get; set; }
    public int PilotosActivos { get; set; }
    public int OrdenesAbiertas { get; set; }
    public int OrdenesHoy { get; set; }
    public int EntregadasHoy { get; set; }
    public int SinGestor { get; set; }
    public DateTime GeneradoAt { get; set; }
}
