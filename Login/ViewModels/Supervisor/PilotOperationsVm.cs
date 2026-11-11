namespace Login.ViewModels.Supervisor;

public sealed class PilotOperationsVm
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Telefono { get; set; } = "";
    public bool Activo { get; set; }
    public bool EnLinea { get; set; }
    public int OrdenesActivas { get; set; }
    public string Estado => !Activo ? "Inactivo" : OrdenesActivas > 0 ? "Con entregas" : "Sin entregas";
    public string Disponibilidad => !Activo ? "Inactivo" : EnLinea ? "Recibe ofertas automáticas" : "Recepción automática pausada";
}

public sealed class OrderDetailVm
{
    public int Id { get; set; }
    public string CodigoA { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Tienda { get; set; } = "";
    public string Destinatario { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string? Piloto { get; set; }
    public decimal? Precio { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderEventVm> Eventos { get; set; } = new();
}

public sealed class OrderEventVm
{
    public DateTime CreatedAt { get; set; }
    public string Tipo { get; set; } = "";
    public string Rol { get; set; } = "";
    public string Descripcion { get; set; } = "";
}
