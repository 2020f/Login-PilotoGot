using System.ComponentModel.DataAnnotations;
using Login.Application.Services;
using Login.Domain.Entities;
using Login.Domain.Saas;

namespace Login.ViewModels.SuperAdmin;

public sealed class EmpresaDetalleVm
{
    public ClienteApp Empresa { get; set; } = null!;
    public List<Plan> Planes { get; set; } = new();
    public QuotaPolicyService.EffectiveQuota Cuotas { get; set; } = null!;
    public int Comercios { get; set; }
    public int Pilotos { get; set; }
    public int Usuarios { get; set; }
    public int OrdenesMes { get; set; }
    public int OrdenesAbiertas { get; set; }
    public string? GestorEmail { get; set; }
    public DateTime Hoy { get; set; }
    public bool Vencida { get; set; }
    public List<SaasInvoice> Facturas { get; set; } = new();
    public List<SaasPlanRequest> Solicitudes { get; set; } = new();
    public List<SaasAuditLog> Actividad { get; set; } = new();
    public string? Ok { get; set; }
    public string? Error { get; set; }
}

public sealed class FacturacionVm
{
    public List<SaasInvoice> Facturas { get; set; } = new();
    public List<ClienteApp> Empresas { get; set; } = new();
    public DateTime Hoy { get; set; }
    public int? EmpresaId { get; set; }
    public string? Estado { get; set; }
    public int Pagina { get; set; }
    public int Total { get; set; }
    public decimal Pendiente { get; set; }
    public decimal Cobrado { get; set; }
    public int Vencidas { get; set; }
    public string? Ok { get; set; }
    public string? Error { get; set; }
}

public sealed class CrearFacturaVm
{
    [Range(1,int.MaxValue)] public int EmpresaId { get; set; }
    [Required,MaxLength(200)] public string Concepto { get; set; } = "Suscripción mensual Envirapido";
    [Required,DataType(DataType.Date)] public DateTime Inicio { get; set; }
    [Required,DataType(DataType.Date)] public DateTime Fin { get; set; }
    [Required,DataType(DataType.Date)] public DateTime Vencimiento { get; set; }
    [Range(typeof(decimal),"0.01","999999999")] public decimal Importe { get; set; }
    [Required,MaxLength(64)] public string Clave { get; set; } = Guid.NewGuid().ToString("N");
    public List<ClienteApp> Empresas { get; set; } = new();
}

public sealed class FacturaDetalleVm
{
    public SaasInvoice Factura { get; set; } = null!;
    public string Empresa { get; set; } = "";
    public DateTime Hoy { get; set; }
    public string? Ok { get; set; }
    public string? Error { get; set; }
}

public sealed class RegistrarPagoVm
{
    [Range(1,int.MaxValue)] public int EmpresaId { get; set; }
    [Range(1,int.MaxValue)] public int FacturaId { get; set; }
    [Range(typeof(decimal),"0.01","999999999")] public decimal Importe { get; set; }
    [DataType(DataType.Date)] public DateTime Fecha { get; set; }
    [Required,MaxLength(40)] public string Metodo { get; set; } = "Transferencia";
    [Required,MaxLength(120)] public string Referencia { get; set; } = "";
    public bool Confirmado { get; set; }
    [Required,MaxLength(64)] public string Clave { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class CambiarPlanVm
{
    [Range(1,int.MaxValue)] public int EmpresaId { get; set; }
    [Range(1,int.MaxValue)] public int PlanId { get; set; }
    public bool RestablecerLimites { get; set; }
    [Required,MaxLength(500)] public string Motivo { get; set; } = "";
    [Required,MaxLength(64)] public string Clave { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class AuditoriaVm
{
    public List<SaasAuditLog> Registros { get; set; } = new();
    public int? EmpresaId { get; set; }
    public string? Q { get; set; }
    public int Pagina { get; set; }
    public int Total { get; set; }
}

public sealed class SistemaVm
{
    public string RuntimeVersion { get; set; } = "";
    public bool RuntimeEnSoporte { get; set; }
    public bool BaseDisponible { get; set; }
    public bool MigracionesPendientes { get; set; }
    public string Entorno { get; set; } = "";
    public DateTime ComprobadoAt { get; set; }
    public bool CorreoConfigurado { get; set; }
    public bool MapasConfigurados { get; set; }
    public int UsuariosBloqueados { get; set; }
    public int SuperAdministradores { get; set; }
}

public sealed class SuscripcionVm
{
    public bool Vencida { get; set; }
    public ClienteApp Empresa { get; set; } = null!;
    public List<Plan> Planes { get; set; } = new();
    public List<SaasPlanRequest> Solicitudes { get; set; } = new();
    public List<SaasInvoice> Facturas { get; set; } = new();
    public DateTime Hoy { get; set; }
    public string? Ok { get; set; }
    public string? Error { get; set; }
}

public sealed class AccesosVm
{
    public List<AccesoUsuarioVm> Usuarios { get; set; } = new();
    public string? Q { get; set; }
    public int Pagina { get; set; }
    public int Total { get; set; }
    public string? Ok { get; set; }
    public string? Error { get; set; }
}
public sealed class AccesoUsuarioVm
{
    public string Id { get; set; } = "";
    public string? Email { get; set; }
    public string Roles { get; set; } = "";
    public string Empresas { get; set; } = "";
    public int SesionesMoviles { get; set; }
    public bool Bloqueado { get; set; }
    public bool DobleFactor { get; set; }
    public bool Protegido { get; set; }
}
