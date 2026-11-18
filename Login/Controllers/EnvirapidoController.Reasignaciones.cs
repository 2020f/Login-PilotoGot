using Login.Application.Operations;
using Login.Domain.Entities;
using Login.Domain.Operations;
using Login.ViewModels.Envirapido;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers;

public sealed partial class EnvirapidoController
{
    [HttpGet("Reasignaciones")]
    public Task<IActionResult> Reasignaciones(int? ordenId = null, string? pedido = null)
        => ReasignacionesViewAsync(ordenId, pedido, false);

    [HttpGet("ReasignacionesFragmento")]
    public Task<IActionResult> ReasignacionesFragmento(int? ordenId = null)
        => ReasignacionesViewAsync(ordenId, null, true);

    private async Task<IActionResult> ReasignacionesViewAsync(int? ordenId, string? pedido, bool fragment)
    {
        var tenant = await TenantAsync(); if (tenant is null) return Forbid();
        if (!string.IsNullOrWhiteSpace(pedido))
        {
            var code = pedido.Trim().TrimStart('#');
            if (code.Length > 80) return BadRequest("El código de pedido no es válido.");
            var legacyNumber = int.TryParse(code, out var parsed) && parsed > 0 ? parsed : -1;
            ordenId = await db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == tenant.Id && (o.CodigoA == code || o.CodigoA == null && o.NumeroOrdenA == legacyNumber))
                .Select(o => (int?)o.Id).SingleOrDefaultAsync();
            if (!ordenId.HasValue) return NotFound();
        }
        if (ordenId.HasValue && !await db.OrdenesEntrega.AnyAsync(o => o.Id == ordenId && o.ClienteAppId == tenant.Id)) return NotFound();
        var actor = await operations.ManagerAsync(Actor);
        var vm = new ReasignacionesPanelVm
        {
            Empresa = tenant.NombreComercial, OrdenId = ordenId, Pedido = pedido, ActualizadoAt = DateTime.UtcNow,
            Datos = await operations.ReasignacionesAsync(actor, ordenId),
            Pilotos = await db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == tenant.Id && p.Activo).OrderBy(p => p.Nombre)
                .Select(p => new SelectListItem(p.Nombre, p.Id.ToString())).ToListAsync(),
            Comercios = await db.Tiendas.AsNoTracking().Where(t => t.ClienteAppId == tenant.Id && t.Activo).OrderBy(t => t.Nombre)
                .Select(t => new SelectListItem(t.Nombre, t.Id.ToString())).ToListAsync(),
            PilotosNombres = await db.Pilotos.AsNoTracking().Where(p => p.ClienteAppId == tenant.Id).ToDictionaryAsync(p => p.Id, p => p.Nombre)
        };
        ViewData["Empresa"] = vm.Empresa; ViewData["Seccion"] = "Pedidos";
        return fragment ? PartialView("_ReasignacionesContenido", vm) : View("Reasignaciones", vm);
    }

    [HttpGet("BuscarParadasRuta")]
    public async Task<IActionResult> BuscarParadasRuta(int pilotoId, string? buscar = null)
    {
        var tenant = await TenantAsync(); if (tenant is null) return Forbid();
        if (!await db.Pilotos.AnyAsync(p => p.Id == pilotoId && p.ClienteAppId == tenant.Id && p.Activo)) return NotFound();
        buscar = buscar?.Trim(); if (buscar?.Length > 100) return BadRequest();
        var query = db.ParadasEntrega.AsNoTracking().Where(p => p.ClienteAppId == tenant.Id && p.OrdenEntrega.GestionPorParadas
            && p.Estado == EstadoParadaEntrega.Pendiente && !p.EnReasignacion
            && (!p.PilotoAsignadoId.HasValue || p.PilotoAsignadoId == pilotoId)
            && (!p.PilotoCustodioId.HasValue || p.PilotoCustodioId == pilotoId && p.RecogidaParadaAt.HasValue)
            && !db.RutaOrdenesOperativas.Any(m => m.ClienteAppId == tenant.Id && m.Vigente
                && (m.ParadaEntregaId == p.Id || !m.ParadaEntregaId.HasValue && m.OrdenEntregaId == p.OrdenEntregaId)));
        if (!string.IsNullOrWhiteSpace(buscar)) query = query.Where(p => p.Nombre.Contains(buscar) || p.OrdenEntrega.CodigoA!.Contains(buscar) || p.OrdenEntrega.Tienda.Nombre.Contains(buscar));
        return Json(await query.OrderByDescending(p => p.OrdenEntregaId).ThenBy(p => p.Posicion).Take(100)
            .Select(p => new { ordenId = p.OrdenEntregaId, codigoA = p.OrdenEntrega.CodigoA, paradaId = p.Id,
                posicion = p.Posicion, destinatario = p.Nombre, recibida = p.RecogidaParadaAt.HasValue }).ToListAsync());
    }
}
