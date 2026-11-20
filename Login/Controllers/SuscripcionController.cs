using System.Security.Claims;
using Login.Application.Services;
using Login.Data;
using Login.ViewModels.SuperAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers;

[Authorize(Roles = "Gestor")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SuscripcionController(ApplicationDbContext db, SaasBillingService billing) : Controller
{
    // La empresa jamás proviene del formulario, querystring o de claims editables.
    private Task<int> TenantAsync() => db.ClientesApp.Where(t => t.GestorIdentityUserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
        .Select(t => t.Id).SingleOrDefaultAsync();

    [HttpGet]
    public async Task<IActionResult> Index(string? ok = null, string? error = null)
    {
        var id = await TenantAsync();
        if (id == 0) return Forbid();
        return View(new SuscripcionVm
        {
            Empresa = await db.ClientesApp.AsNoTracking().Include(t => t.Plan).SingleAsync(t => t.Id == id), Hoy = billing.Today, Vencida = await billing.IsExpiredAsync(id),
            Planes = await db.Planes.AsNoTracking().Where(p => p.Activo).OrderBy(p => p.PrecioMensual).ToListAsync(),
            Facturas = await db.SaasInvoices.AsNoTracking().Include(i => i.Payments).Where(i => i.ClienteAppId == id).OrderByDescending(i => i.Id).Take(30).ToListAsync(),
            Solicitudes = await db.SaasPlanRequests.AsNoTracking().Where(r => r.ClienteAppId == id).OrderByDescending(r => r.Id).Take(20).ToListAsync(), Ok = ok, Error = error
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SolicitarPlan(int planId, string motivo, string clave)
    {
        var id = await TenantAsync();
        if (id == 0) return Forbid();
        try { await billing.RequestPlanChangeAsync(id, planId, motivo, User.FindFirstValue(ClaimTypes.NameIdentifier)!, clave); }
        catch (InvalidOperationException ex) { return RedirectToAction(nameof(Index), new { error = ex.Message }); }
        return RedirectToAction(nameof(Index), new { ok = "Solicitud enviada al administrador de la plataforma. Tu operación continúa con el plan actual hasta su aprobación." });
    }
}
