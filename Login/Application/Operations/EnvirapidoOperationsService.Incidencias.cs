using Login.Domain.Entities;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    public async Task<object> ReportarIncidenciaAsync(OperationsActor a, ReportarIncidenciaRequest r)
        => await MutateAsync(a, "ReportarIncidencia", r.OperacionId, r, async () =>
        {
            var category = Required(r.Categoria, 80, "Categoría");
            var description = Required(r.Descripcion, 1000, "Descripción");
            if (!r.OrdenId.HasValue && !r.RutaId.HasValue) throw new OperationsException(400, "Selecciona la orden o ruta afectada.");
            if (r.ParadaId.HasValue && !r.OrdenId.HasValue) throw new OperationsException(400, "La parada necesita su orden.");
            OrdenEntrega? order = r.OrdenId.HasValue ? await OrderAsync(a, r.OrdenId.Value) : null;
            if (r.ParadaId.HasValue && !order!.Paradas.Any(p => p.Id == r.ParadaId)) throw new OperationsException(404, "Parada no disponible.");
            var route = r.RutaId.HasValue ? await LoadRouteAsync(a, r.RutaId.Value) : null;
            if (route is null && order?.PilotoId is int assignedPilot)
            {
                // Dentro del bloqueo del tenant: capturar el contexto activo del
                // reporte, sin enlazar recorridos históricos o de otro custodio.
                var currentRoute = await db.RutaOrdenesOperativas.AsNoTracking()
                    .Where(m => m.ClienteAppId == a.TenantId && m.OrdenEntregaId == order.Id && m.Vigente
                        && m.Ruta.Estado == EstadoRutaOperativa.Activa && m.Ruta.PilotoId == assignedPilot)
                    .Select(m => (int?)m.RutaId).SingleOrDefaultAsync();
                if (currentRoute.HasValue) route = await LoadRouteAsync(a, currentRoute.Value);
            }
            if (route is not null && order is not null && !route.Ordenes.Any(o => o.OrdenEntregaId == order.Id)) throw new OperationsException(400, "La orden no pertenece a la ruta seleccionada.");
            var rule = await db.ReglasIncidencia.SingleOrDefaultAsync(x => x.ClienteAppId == a.TenantId && x.Categoria == category && x.Activa);
            var incident = new IncidenciaOperativa { ClienteAppId = a.TenantId, OrdenEntregaId = r.OrdenId,
                ParadaEntregaId = r.ParadaId, RutaId = route?.Id, Categoria = category, Descripcion = description,
                ReportadaPor = a.UserId, Prioridad = rule?.Prioridad ?? 2, Efecto = rule?.Efecto ?? EfectoIncidencia.Continuar,
                VenceAt = rule?.MinutosResolucion is int minutes ? Now.AddMinutes(minutes) : null,
                ReglaVersion = rule?.Version, CreatedAt = Now, UpdatedAt = Now };
            incident.Actividad.Add(new() { ClienteAppId = a.TenantId, ActorId = a.UserId, Accion = "Registro",
                Texto = rule is null ? "Reporte registrado sin política automática de pausa ni plazo. Requiere atención del gestor." : "Reporte registrado con la regla de atención vigente.", CreatedAt = Now });
            db.IncidenciasOperativas.Add(incident); await db.SaveChangesAsync();
            Audit(a, "Incidencia", incident.Id, $"Registrada: {category}.");
            return new { id = incident.Id, version = incident.Version, estado = incident.Estado.ToString(), efecto = incident.Efecto.ToString() };
        });

    public async Task<object> CambiarIncidenciaAsync(OperationsActor a, int id, CambiarIncidenciaRequest r)
        => await MutateAsync(a, "CambiarIncidencia", r.OperacionId, new { id, r }, async () =>
        {
            var incident = await db.IncidenciasOperativas.Include(i => i.Actividad).SingleOrDefaultAsync(i => i.Id == id && i.ClienteAppId == a.TenantId)
                ?? throw new OperationsException(404, "Incidencia no disponible.");
            if (a.EsPiloto)
            {
                if (r.Accion != "Nota") throw new OperationsException(403, "El piloto puede aportar información; la gestión corresponde al responsable autorizado.");
                var owns = incident.ReportadaPor == a.UserId
                    || (incident.OrdenEntregaId.HasValue && await db.OrdenesEntrega.AnyAsync(o => o.Id == incident.OrdenEntregaId && o.ClienteAppId == a.TenantId && o.PilotoId == a.PilotoId))
                    || (incident.RutaId.HasValue && await db.RutasOperativas.AnyAsync(x => x.Id == incident.RutaId && x.ClienteAppId == a.TenantId && x.PilotoId == a.PilotoId));
                if (!owns) throw new OperationsException(404, "Incidencia no disponible.");
            }
            CheckVersion(incident, r.Version);
            var text = Required(r.Texto, 1000, "Nota o motivo");
            switch (r.Accion)
            {
                case "Nota": break;
                case "Asignar":
                    var owner = await db.ClientesApp.Where(t => t.Id == a.TenantId).Select(t => t.GestorIdentityUserId).SingleAsync();
                    if (string.IsNullOrWhiteSpace(r.ResponsableId) || r.ResponsableId != owner) throw new OperationsException(400, "Selecciona un responsable autorizado de esta empresa.");
                    incident.ResponsableId = r.ResponsableId; if (incident.Estado == EstadoIncidencia.Nueva) incident.Estado = EstadoIncidencia.EnAtencion; break;
                case "Atender":
                    if (incident.Estado == EstadoIncidencia.Resuelta) throw new OperationsException(400, "Reabre el caso antes de atenderlo.");
                    incident.Estado = EstadoIncidencia.EnAtencion; incident.ResponsableId ??= a.UserId; break;
                case "Escalar":
                    if (incident.Estado == EstadoIncidencia.Resuelta) throw new OperationsException(400, "El caso ya está resuelto.");
                    incident.Estado = EstadoIncidencia.Escalada; incident.ResponsableId ??= a.UserId; break;
                case "Resolver":
                    if (incident.Estado == EstadoIncidencia.Resuelta) throw new OperationsException(400, "El caso ya está resuelto.");
                    incident.Estado = EstadoIncidencia.Resuelta; incident.Resolucion = text; incident.ResueltaAt = Now; incident.ResponsableId ??= a.UserId; break;
                case "Reabrir":
                    if (incident.Estado != EstadoIncidencia.Resuelta) throw new OperationsException(400, "El caso sigue abierto.");
                    incident.Estado = EstadoIncidencia.EnAtencion; incident.ResueltaAt = null; incident.Resolucion = null; break;
                default: throw new OperationsException(400, "Acción de incidencia no válida.");
            }
            Touch(incident);
            incident.Actividad.Add(new() { ClienteAppId = a.TenantId, Accion = r.Accion, Texto = text, ActorId = a.UserId, CreatedAt = Now });
            Audit(a, "Incidencia", id, r.Accion + ": " + text);
            return new { id, version = incident.Version, estado = incident.Estado.ToString() };
        });

    public async Task<object> GuardarReglaIncidenciaAsync(OperationsActor a, GuardarReglaIncidenciaRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "ReglaIncidencia", r.OperacionId, r, async () =>
        {
            var category = Required(r.Categoria, 80, "Categoría");
            var effect = EnumValue<EfectoIncidencia>(r.Efecto);
            if (r.Prioridad is < 1 or > 4 || r.MinutosResolucion is < 1 or > 43200) throw new OperationsException(400, "Prioridad o plazo no válido.");
            if (r.DetectarDemora && r.MinutosResolucion is null) throw new OperationsException(400, "Configura el plazo de atención para la detección de demora.");
            if (await db.ReglasIncidencia.AnyAsync(x => x.ClienteAppId == a.TenantId && x.Categoria == category && x.Id != r.Id)) throw new OperationsException(400, "Ya hay una regla para esa categoría.");
            var rule = r.Id.HasValue ? await db.ReglasIncidencia.SingleOrDefaultAsync(x => x.Id == r.Id && x.ClienteAppId == a.TenantId) ?? throw new OperationsException(404, "Regla no disponible.") : new ReglaIncidenciaOperativa { ClienteAppId = a.TenantId, CreatedAt = Now };
            if (r.Id.HasValue) CheckVersion(rule, r.Version); else db.ReglasIncidencia.Add(rule);
            rule.Categoria = category; rule.Prioridad = r.Prioridad; rule.MinutosResolucion = r.MinutosResolucion;
            rule.Efecto = effect; rule.Activa = r.Activa; rule.DetectarDemora = r.DetectarDemora; Touch(rule);
            await db.SaveChangesAsync(); Audit(a, "Regla", rule.Id, "Regla de atención guardada; los casos existentes conservan la política aplicada.");
            return new { id = rule.Id, version = rule.Version };
        });
    }

    public Task<object> DetectarDemorasAsync(OperationsActor a, string operationId)
        => DetectarDemorasCoreAsync(a, operationId, false);

    internal async Task DetectarDemorasAutomaticasAsync(OperationsActor a)
    {
        await EnsureActorAsync(a, true);
        // Avoid writing a command receipt when no new overdue work exists.
        if (!await db.DatosEnviosOperativos.AnyAsync(d => d.ClienteAppId == a.TenantId && d.CompromisoEntrega < Now
                && d.CanceladaAt == null && !EstadosEntrega.Terminales.Contains(d.OrdenEntrega.Estado)
                && !db.IncidenciasOperativas.Any(i => i.ClienteAppId == a.TenantId && i.ClaveDeteccion == "Demora:" + d.OrdenEntregaId))) return;
        await DetectarDemorasCoreAsync(a, Guid.NewGuid().ToString("N"), true);
    }

    // Unique detection keys make scheduled scans safe across workers and refreshes.
    private async Task<object> DetectarDemorasCoreAsync(OperationsActor a, string operationId, bool automatic)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "DetectarDemoras", operationId, new { accion = "DetectarDemoras", automatic }, async () =>
        {
            var rules = await db.ReglasIncidencia.Where(r => r.ClienteAppId == a.TenantId && r.Activa && r.DetectarDemora).ToListAsync();
            if (rules.Count > 1) throw new OperationsException(400, "Activa una sola regla de detección automática de demora para evitar políticas ambiguas.");
            if (rules.Count == 0) return new { creadas = 0 };
            var rule = rules[0];
            var orders = await db.DatosEnviosOperativos.Where(d => d.ClienteAppId == a.TenantId && d.CompromisoEntrega < Now && d.CanceladaAt == null && !EstadosEntrega.Terminales.Contains(d.OrdenEntrega.Estado)).ToListAsync();
            var created = 0;
            foreach (var data in orders)
            {
                var key = $"Demora:{data.OrdenEntregaId}";
                if (await db.IncidenciasOperativas.AnyAsync(i => i.ClienteAppId == a.TenantId && i.ClaveDeteccion == key)) continue;
                var incident = new IncidenciaOperativa { ClienteAppId = a.TenantId, OrdenEntregaId = data.OrdenEntregaId,
                    Categoria = rule.Categoria, Descripcion = "Compromiso de entrega vencido.", Prioridad = rule.Prioridad,
                    Efecto = rule.Efecto, ReglaVersion = rule.Version, ReportadaPor = automatic ? "System" : a.UserId, ClaveDeteccion = key,
                    VenceAt = rule.MinutosResolucion is int minutes ? Now.AddMinutes(minutes) : null, CreatedAt = Now, UpdatedAt = Now };
                incident.Actividad.Add(new() { ClienteAppId = a.TenantId, ActorId = automatic ? "System" : a.UserId, Accion = "DeteccionAutomatica", Texto = "Detectada a partir del compromiso de entrega registrado.", CreatedAt = Now });
                db.IncidenciasOperativas.Add(incident); created++;
            }
            Audit(automatic ? a with { UserId = "System" } : a, "Incidencia", 0, $"Detección completada: {created} casos nuevos.");
            return new { creadas = created };
        });
    }
}
