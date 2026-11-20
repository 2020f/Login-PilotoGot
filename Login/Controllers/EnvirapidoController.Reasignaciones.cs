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
    public Task<IActionResult> Reasignaciones(int? ordenId = null, string? pedido = null, int? incidenciaId = null,
        int? paradaId = null, string? accion = null, string vista = "activas")
        => ReasignacionesViewAsync(ordenId, pedido, false, incidenciaId, paradaId, accion, vista);

    [HttpGet("ReasignacionesFragmento")]
    public Task<IActionResult> ReasignacionesFragmento(int? ordenId = null, int? incidenciaId = null,
        int? paradaId = null, string vista = "activas")
        => ReasignacionesViewAsync(ordenId, null, true, incidenciaId, paradaId, null, vista);

    private async Task<IActionResult> ReasignacionesViewAsync(int? ordenId, string? pedido, bool fragment,
        int? incidenciaId, int? paradaId, string? accion, string vista)
    {
        var tenant = await TenantAsync(); if (tenant is null) return Forbid();
        if (vista is not ("activas" or "porreasignar") || accion is not (null or "retiros" or "reasignar" or "cancelar-paradas"))
            return BadRequest("La vista o acción de gestión no es válida.");
        if (!string.IsNullOrWhiteSpace(pedido))
        {
            var code = pedido.Trim().TrimStart('#');
            if (code.StartsWith("EV-", StringComparison.OrdinalIgnoreCase)) code = code[3..];
            if (code.Length > 80) return BadRequest("El código de pedido no es válido.");
            var legacyNumber = int.TryParse(code, out var parsed) && parsed > 0 ? parsed : -1;
            ordenId = await db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == tenant.Id && (o.CodigoA == code || o.CodigoA == null && o.NumeroOrdenA == legacyNumber))
                .Select(o => (int?)o.Id).SingleOrDefaultAsync();
            if (!ordenId.HasValue) return NotFound();
        }
        if (incidenciaId.HasValue)
        {
            var incident = await db.IncidenciasOperativas.AsNoTracking()
                .Where(i => i.Id == incidenciaId && i.ClienteAppId == tenant.Id)
                .Select(i => new { i.OrdenEntregaId, i.ParadaEntregaId }).SingleOrDefaultAsync();
            if (incident?.OrdenEntregaId is not int incidentOrder || ordenId.HasValue && ordenId != incidentOrder
                || incident.ParadaEntregaId.HasValue && paradaId.HasValue && paradaId != incident.ParadaEntregaId) return NotFound();
            ordenId = incidentOrder; paradaId = incident.ParadaEntregaId ?? paradaId;
        }
        var order = ordenId.HasValue ? await db.OrdenesEntrega.AsNoTracking()
            .Where(o => o.Id == ordenId && o.ClienteAppId == tenant.Id)
            .Select(o => new { o.Id, o.CodigoA, o.NumeroOrdenA }).SingleOrDefaultAsync() : null;
        if (ordenId.HasValue && order is null) return NotFound();
        if (accion is not null && !ordenId.HasValue) return BadRequest("Selecciona el pedido antes de gestionar sus paradas.");
        if (paradaId.HasValue && (!ordenId.HasValue || !await db.ParadasEntrega.AnyAsync(p =>
            p.Id == paradaId && p.OrdenEntregaId == ordenId && p.ClienteAppId == tenant.Id))) return NotFound();
        var actor = await operations.ManagerAsync(Actor);
        var snapshot = await operations.ReasignacionesAsync(actor, ordenId, incluirActivas: true);
        if (paradaId.HasValue) snapshot = snapshot with { Paradas = snapshot.Paradas.Where(p => p.ParadaId == paradaId).ToList() };
        if (vista == "porreasignar") snapshot = snapshot with
        { Paradas = snapshot.Paradas.Where(p => p.EnReasignacion && !p.PilotoAsignadoId.HasValue).ToList() };
        var vm = new ReasignacionesPanelVm
        {
            Empresa = tenant.NombreComercial, OrdenId = ordenId, Pedido = pedido, ActualizadoAt = DateTime.UtcNow,
            CodigoPedido = order?.CodigoA ?? order?.NumeroOrdenA.ToString(), IncidenciaId = incidenciaId,
            ParadaId = paradaId, AccionInicial = accion, Vista = vista, Datos = snapshot,
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
