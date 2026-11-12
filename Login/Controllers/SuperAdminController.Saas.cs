using System.Security.Claims;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Saas;
using Login.ViewModels.SuperAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers;

public partial class SuperAdminController
{
    [HttpGet]
    public async Task<IActionResult> DetalleCliente(int id, string? ok = null, string? error = null)
    {
        var tenant = await _db.ClientesApp.AsNoTracking().Include(c => c.Plan).SingleOrDefaultAsync(c => c.Id == id);
        if (tenant is null) return NotFound();
        var start = DateTime.SpecifyKind(new DateTime(_billing.Today.Year, _billing.Today.Month, 1).AddHours(4), DateTimeKind.Utc);
        return View(new EmpresaDetalleVm
        {
            Empresa = tenant, Planes = await _db.Planes.AsNoTracking().Where(p => p.Activo).OrderBy(p => p.PrecioMensual).ToListAsync(),
            Cuotas = await _quotas.ResolveAsync(id), Comercios = await _db.Tiendas.CountAsync(t => t.ClienteAppId == id && t.Activo),
            Pilotos = await _db.Pilotos.CountAsync(p => p.ClienteAppId == id && p.Activo), Usuarios = await _quotas.CountUsersAsync(id),
            OrdenesMes = await _db.OrdenesEntrega.CountAsync(o => o.ClienteAppId == id && o.CreatedAt >= start && o.CreatedAt < start.AddMonths(1)),
            OrdenesAbiertas = await _db.OrdenesEntrega.CountAsync(o => o.ClienteAppId == id && !Login.Domain.Operations.EstadosEntrega.Terminales.Contains(o.Estado)),
            GestorEmail = await _db.Users.Where(u => u.Id == tenant.GestorIdentityUserId).Select(u => u.Email).SingleOrDefaultAsync(),
            Hoy = _billing.Today, Vencida = await _billing.IsExpiredAsync(id),
            Facturas = await _db.SaasInvoices.AsNoTracking().Include(i => i.Payments).Where(i => i.ClienteAppId == id).OrderByDescending(i => i.Id).Take(30).ToListAsync(),
            Solicitudes = await _db.SaasPlanRequests.AsNoTracking().Include(r=>r.RequestedPlan).Where(r => r.ClienteAppId == id).OrderByDescending(r => r.Id).Take(30).ToListAsync(),
            Actividad = await _db.SaasAuditLogs.AsNoTracking().Where(a => a.ClienteAppId == id).OrderByDescending(a => a.Id).Take(20).ToListAsync(), Ok = ok, Error = error
        });
    }

    [HttpGet]
    public async Task<IActionResult> Facturacion(int? empresaId = null, string? estado = null, int pagina = 1, string? ok = null, string? error = null)
    {
        pagina = Math.Clamp(pagina, 1, 1000000);
        var query = _db.SaasInvoices.AsNoTracking().Include(i => i.Payments).Include(i=>i.ClienteApp).AsQueryable();
        if (empresaId.HasValue) query = query.Where(i => i.ClienteAppId == empresaId.Value);
        // Las etiquetas se derivan exclusivamente del libro de pagos, nunca de un checkbox de edición.
        var amounts = await query.Select(i => new { i.Id, i.Amount, i.VoidedAt, i.DueDate,
            Payments = i.Payments.Where(p => p.VoidedAt == null).Select(p => p.Amount).ToList() }).ToListAsync();
        string Status(decimal amount, decimal paid, DateTime due, DateTime? voided) => voided != null ? "Anulada" : paid >= amount ? "Pagada" : due.Date < _billing.Today ? "Vencida" : paid > 0 ? "Parcial" : "Pendiente";
        var ids = amounts.Where(i => string.IsNullOrEmpty(estado) || Status(i.Amount, i.Payments.Sum(), i.DueDate, i.VoidedAt) == estado).Select(i => i.Id).ToArray();
        // Limitar los IDs al lote de página evita un IN masivo en SQL Server.
        var pageIds = ids.OrderByDescending(i => i).Skip((pagina - 1) * 20).Take(20).ToArray();
        return View(new FacturacionVm
        {
            Facturas = await query.Where(i => pageIds.Contains(i.Id)).OrderByDescending(i => i.Id).ToListAsync(),
            Empresas = await _db.ClientesApp.AsNoTracking().OrderBy(c => c.NombreComercial).ToListAsync(),
            Hoy = _billing.Today, EmpresaId = empresaId, Estado = estado, Pagina = pagina, Total = ids.Length,
            Pendiente = amounts.Where(i => i.VoidedAt == null).Sum(i => Math.Max(0, i.Amount - i.Payments.Sum())),
            Cobrado = amounts.Sum(i => i.Payments.Sum()), Vencidas = amounts.Count(i => Status(i.Amount, i.Payments.Sum(), i.DueDate, i.VoidedAt) == "Vencida"), Ok = ok, Error = error
        });
    }

    [HttpGet]
    public async Task<IActionResult> CrearFactura(int? empresaId = null)
    {
        var tenant = empresaId.HasValue ? await _db.ClientesApp.AsNoTracking().Include(c => c.Plan).SingleOrDefaultAsync(c => c.Id == empresaId) : null;
        return View(new CrearFacturaVm { EmpresaId = tenant?.Id ?? 0, Importe = tenant?.Plan.PrecioMensual ?? 0,
            Inicio = _billing.Today, Fin = _billing.Today.AddMonths(1).AddDays(-1), Vencimiento = _billing.Today.AddDays(7),
            Empresas = await _db.ClientesApp.AsNoTracking().OrderBy(c => c.NombreComercial).ToListAsync() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearFactura(CrearFacturaVm input)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await _billing.IssueInvoiceAsync(input.EmpresaId, input.Inicio, input.Fin, input.Vencimiento, input.Importe, input.Concepto, GetAuditUser(), input.Clave);
                return RedirectToAction(nameof(Factura), new { id, ok = "Factura emitida. El pago continúa pendiente hasta su confirmación." });
            }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        input.Empresas = await _db.ClientesApp.AsNoTracking().OrderBy(c => c.NombreComercial).ToListAsync();
        return View(input);
    }

    [HttpGet]
    public async Task<IActionResult> Factura(int id, string? ok = null, string? error = null)
    {
        var invoice = await _db.SaasInvoices.AsNoTracking().Include(i => i.Payments).SingleOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        return View(new FacturaDetalleVm { Factura = invoice, Hoy = _billing.Today,
            Empresa = await _db.ClientesApp.Where(c => c.Id == invoice.ClienteAppId).Select(c => c.NombreComercial).SingleAsync(), Ok = ok, Error = error });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarPago(RegistrarPagoVm input)
    {
        if (!input.Confirmado) ModelState.AddModelError("", "Confirma que recibiste el pago fuera del sistema antes de registrarlo.");
        if (!ModelState.IsValid) return RedirectToAction(nameof(Factura), new { id = input.FacturaId, error = "Revisa importe, fecha, referencia y confirmación del pago recibido." });
        try
        {
            await _billing.RecordPaymentAsync(input.EmpresaId, input.FacturaId, input.Importe, input.Fecha, input.Metodo, input.Referencia, GetAuditUser(), input.Clave);
            return RedirectToAction(nameof(Factura), new { id = input.FacturaId, ok = "Pago recibido registrado y conciliado." });
        }
        catch (InvalidOperationException ex) { return RedirectToAction(nameof(Factura), new { id = input.FacturaId, error = ex.Message }); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AnularPago(int empresaId, int pagoId, string motivo, string clave)
    {
        var invoiceId = await _db.SaasPayments.Where(p => p.Id == pagoId && p.ClienteAppId == empresaId).Select(p => (int?)p.InvoiceId).SingleOrDefaultAsync();
        if (invoiceId is null) return NotFound();
        try { await _billing.VoidPaymentAsync(empresaId, pagoId, motivo, GetAuditUser(), clave); }
        catch (InvalidOperationException ex) { return RedirectToAction(nameof(Factura), new { id = invoiceId, error = ex.Message }); }
        return RedirectToAction(nameof(Factura), new { id = invoiceId, ok = "Pago anulado con motivo registrado; saldo y cobertura recalculados." });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AnularFactura(int empresaId, int facturaId, string motivo, string clave)
    {
        try { await _billing.VoidInvoiceAsync(empresaId, facturaId, motivo, GetAuditUser(), clave); }
        catch (InvalidOperationException ex) { return RedirectToAction(nameof(Factura), new { id = facturaId, error = ex.Message }); }
        return RedirectToAction(nameof(Factura), new { id = facturaId, ok = "Factura anulada y conservada en auditoría." });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarPlan(CambiarPlanVm input)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(DetalleCliente), new { id = input.EmpresaId, error = "Selecciona el plan y explica el motivo del cambio." });
        try { await _billing.ChangePlanAsync(input.EmpresaId, input.PlanId, input.RestablecerLimites, input.Motivo, GetAuditUser(), input.Clave); }
        catch (InvalidOperationException ex) { return RedirectToAction(nameof(DetalleCliente), new { id = input.EmpresaId, error = ex.Message }); }
        return RedirectToAction(nameof(DetalleCliente), new { id = input.EmpresaId, ok = "Plan actualizado. Las órdenes y rutas conservan su asignación y sus códigos." });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolverSolicitud(int solicitudId, bool aprobar, bool restablecerLimites, string motivo, string clave)
    {
        var tenant = await _db.SaasPlanRequests.Where(r => r.Id == solicitudId).Select(r => (int?)r.ClienteAppId).SingleOrDefaultAsync();
        if (tenant is null) return NotFound();
        try { await _billing.ResolvePlanRequestAsync(solicitudId, aprobar, restablecerLimites, motivo, GetAuditUser(), clave); }
        catch (InvalidOperationException ex) { return RedirectToAction(nameof(DetalleCliente), new { id = tenant, error = ex.Message }); }
        return RedirectToAction(nameof(DetalleCliente), new { id = tenant, ok = "Solicitud de plan resuelta y registrada." });
    }

    [HttpGet]
    public async Task<IActionResult> Auditoria(int? empresaId = null, string? q = null, int pagina = 1)
    {
        pagina = Math.Clamp(pagina, 1, 1000000);
        var query = _db.SaasAuditLogs.AsNoTracking().AsQueryable();
        if (empresaId.HasValue) query = query.Where(a => a.ClienteAppId == empresaId);
        if (!string.IsNullOrWhiteSpace(q)) { q = q.Trim()[..Math.Min(100, q.Trim().Length)]; query = query.Where(a => a.Actor.Contains(q) || a.Action.Contains(q) || a.Detail.Contains(q)); }
        return View(new AuditoriaVm { Registros = await query.OrderByDescending(a => a.Id).Skip((pagina-1)*30).Take(30).ToListAsync(),
            Total = await query.CountAsync(), EmpresaId = empresaId, Q = q, Pagina = pagina });
    }

    [HttpGet]
    public async Task<IActionResult> Sistema([FromServices] IWebHostEnvironment environment, [FromServices] IConfiguration configuration, [FromServices] Login.Services.IAccountEmailSender email)
    {
        bool available = false, pending = true;
        try { available = await _db.Database.CanConnectAsync(); if (available) pending = (await _db.Database.GetPendingMigrationsAsync()).Any(); }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException) { /* No exponer cadenas SQL ni excepciones. */ }
        var vm = new SistemaVm { BaseDisponible = available, MigracionesPendientes = pending, Entorno = environment.EnvironmentName,
            ComprobadoAt = _clock.GetUtcNow().UtcDateTime, CorreoConfigurado = email.IsConfigured,
            MapasConfigurados = !string.IsNullOrWhiteSpace(configuration["GoogleMaps:BrowserApiKey"]),
            RuntimeVersion = Environment.Version.ToString(),
            RuntimeEnSoporte = Environment.Version.Major is 10 or 11 ? _billing.Today <= new DateTime(2028,11,14)
                : Environment.Version.Major is 8 or 9 && _billing.Today <= new DateTime(2026,11,10) };
        if (!available) return View(vm);
        var roleId = await _db.Roles.Where(r => r.Name == "SuperAdmin").Select(r => r.Id).SingleOrDefaultAsync();
        vm.UsuariosBloqueados = await _db.Users.CountAsync(u => u.LockoutEnd > _clock.GetUtcNow());
        vm.SuperAdministradores = await _db.UserRoles.CountAsync(r => r.RoleId == roleId);
        return View(vm);
    }
}
