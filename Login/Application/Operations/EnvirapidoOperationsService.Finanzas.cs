using System.Globalization;
using System.Text.Json;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static void Amount(decimal value, bool signed = false)
    { if (value is < -999999999m or > 999999999m || (!signed && value < 0) || Money(value) != value) throw new OperationsException(400, "El importe debe tener como máximo dos decimales y estar dentro del límite permitido."); }

    public async Task<object> GuardarReglaRemuneracionAsync(OperationsActor a, GuardarReglaRemuneracionRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "ReglaRemuneracion", r.OperacionId, r, async () =>
        {
            var unit = EnumValue<UnidadRemuneracion>(r.Unidad);
            Amount(r.ImporteFijo); Amount(r.IncentivoFijo); Amount(r.PorcentajeTarifa); Amount(r.ComisionPorcentaje);
            if (r.PorcentajeTarifa > 100 || r.ComisionPorcentaje > 100 || (unit == UnidadRemuneracion.Parada && r.PorcentajeTarifa != 0)) throw new OperationsException(400, "Los porcentajes admiten de 0 a 100; una parada requiere importe fijo, sin reutilizar la tarifa completa de la orden.");
            var rule = r.Id.HasValue ? await db.ReglasRemuneracion.SingleOrDefaultAsync(x => x.Id == r.Id && x.ClienteAppId == a.TenantId) ?? throw new OperationsException(404, "Regla no disponible.") : new ReglaRemuneracionOperativa { ClienteAppId = a.TenantId, CreatedAt = Now };
            if (r.Id.HasValue) CheckVersion(rule, r.Version); else db.ReglasRemuneracion.Add(rule);
            rule.Nombre = Required(r.Nombre, 100, "Nombre"); rule.Unidad = unit; rule.ImporteFijo = r.ImporteFijo;
            rule.PorcentajeTarifa = r.PorcentajeTarifa; rule.IncentivoFijo = r.IncentivoFijo; rule.ComisionPorcentaje = r.ComisionPorcentaje; rule.Activa = r.Activa; Touch(rule);
            await db.SaveChangesAsync(); Audit(a, "Regla", rule.Id, "Regla de remuneración guardada. Las liquidaciones emitidas conservan su versión e importes.");
            return new { id = rule.Id, version = rule.Version };
        });
    }

    private (DateTime From, DateTime Until) Period(DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366 || to > Today) throw new OperationsException(400, "El período admite hasta un año y no puede ser futuro.");
        var zone = BusinessZone;
        return (TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone), TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
    }

    public async Task<object> GenerarLiquidacionAsync(OperationsActor a, GenerarLiquidacionRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "GenerarLiquidacion", r.OperacionId, r, async () =>
        {
            var (from, until) = Period(r.Desde, r.Hasta);
            if (r.PilotoId.HasValue == r.TiendaId.HasValue) throw new OperationsException(400, "Selecciona un repartidor o un comercio.");
            // Una selección expresa es exacta, nunca una sugerencia. La validación
            // corre dentro del lock/transacción de MutateAsync antes de emitir nada.
            var selectedCash = r.CobroIds is { Count: > 0 } ? r.CobroIds.ToArray() : null;
            if (selectedCash is not null && (selectedCash.Any(id => id <= 0)
                || selectedCash.Distinct().Count() != selectedCash.Length))
                throw new OperationsException(400, "Selecciona cobros válidos sin duplicados.");
            if (r.PilotoId.HasValue && selectedCash is not null)
                throw new OperationsException(400, "Los cobros de comercios se liquidan por separado de la remuneración del piloto.");
            var settlement = new LiquidacionOperativa { ClienteAppId = a.TenantId, PilotoId = r.PilotoId, TiendaId = r.TiendaId,
                Desde = r.Desde, Hasta = r.Hasta, CreatedAt = Now, UpdatedAt = Now, Conciliada = true };
            db.LiquidacionesOperativas.Add(settlement);
            var cashApplied = new List<CobroOperativo>();
            if (r.PilotoId is int pilot)
            {
                if (!await db.Pilotos.AnyAsync(p => p.Id == pilot && p.ClienteAppId == a.TenantId)) throw new OperationsException(404, "Piloto no disponible.");
                var rule = await db.ReglasRemuneracion.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.ReglaId && x.ClienteAppId == a.TenantId && x.Activa)
                    ?? throw new OperationsException(400, "Selecciona una regla de remuneración activa.");
                var doneOrders = await db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == a.TenantId && o.PilotoId == pilot && o.Estado == EstadoOrden.Entregada && o.EntregadaAt >= from && o.EntregadaAt < until).Include(o => o.Paradas).ToListAsync();
                var doneStops = await db.ParadasEntrega.AsNoTracking().Where(p => p.ClienteAppId == a.TenantId && p.OrdenEntrega.PilotoId == pilot && p.Estado == EstadoParadaEntrega.Entregada && p.EntregadaAt >= from && p.EntregadaAt < until).Include(p => p.OrdenEntrega).ToListAsync();
                var sources = new List<PaySource>();
                if (rule.Unidad == UnidadRemuneracion.Servicio)
                    sources.AddRange(doneOrders.Select(o => new PaySource("Servicio", o.Id, o.Precio, o.Paradas.Select(p => p.Id).ToArray(), o.Id, null)));
                else if (rule.Unidad == UnidadRemuneracion.Parada)
                    sources.AddRange(doneStops.Select(p => new PaySource("Parada", p.Id, null, [p.Id], p.OrdenEntregaId, null)));
                else
                {
                    var routes = await db.RutasOperativas.AsNoTracking().Where(x => x.ClienteAppId == a.TenantId && x.PilotoId == pilot && x.Estado == EstadoRutaOperativa.Finalizada && x.FinalizadaAt >= from && x.FinalizadaAt < until)
                        .Include(x => x.Ordenes).ThenInclude(x => x.OrdenEntrega).ThenInclude(o => o.Paradas).AsSplitQuery().ToListAsync();
                    sources.AddRange(routes.Where(x => x.Ordenes.All(o => o.OrdenEntrega.Estado == EstadoOrden.Entregada)).Select(x => new PaySource("Ruta", x.Id,
                        x.Ordenes.All(o => o.OrdenEntrega.Precio.HasValue) ? x.Ordenes.Sum(o => o.OrdenEntrega.Precio!.Value) : null,
                        x.Ordenes.SelectMany(o => o.OrdenEntrega.Paradas).Select(p => p.Id).ToArray(), null, x.Id)));
                }
                var prefix = $"piloto:{pilot}:cobertura:";
                var used = (await db.MovimientosOperativos.AsNoTracking().Where(m => m.ClienteAppId == a.TenantId && m.Concepto == "Cobertura" && m.ClaveOrigen.StartsWith(prefix)).Select(m => m.ParadaEntregaId).ToListAsync()).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
                var eligible = sources.Where(s => s.Paradas.Length > 0 && s.Paradas.All(id => !used.Contains(id))).ToList();
                if (eligible.Count == 0) throw new OperationsException(400, "No hay operaciones elegibles sin liquidar para ese período y regla.");
                foreach (var source in eligible)
                {
                    if (rule.PorcentajeTarifa != 0 && source.Tarifa is null) throw new OperationsException(400, "Una operación no tiene tarifa comercial confirmada; utiliza importe fijo o registra primero su tarifa.");
                    var basis = Money(rule.ImporteFijo + (source.Tarifa ?? 0) * rule.PorcentajeTarifa / 100);
                    var incentive = rule.IncentivoFijo;
                    var commission = Money(basis * rule.ComisionPorcentaje / 100);
                    AddMovement(a, settlement, "Servicios", basis, $"{source.Unidad} {source.Id}", $"piloto:{pilot}:{source.Unidad}:{source.Id}:servicio", source, rule);
                    if (incentive != 0) AddMovement(a, settlement, "Incentivos", incentive, rule.Nombre, $"piloto:{pilot}:{source.Unidad}:{source.Id}:incentivo", source, rule);
                    if (commission != 0) AddMovement(a, settlement, "Comisiones", -commission, rule.Nombre, $"piloto:{pilot}:{source.Unidad}:{source.Id}:comision", source, rule);
                    foreach (var stop in source.Paradas)
                    {
                        settlement.Movimientos.Add(new MovimientoOperativo { ClienteAppId = a.TenantId, Concepto = "Cobertura", Descripcion = "Unidad de entrega incluida en remuneración.",
                            ClaveOrigen = prefix + stop, ParadaEntregaId = stop, OrdenEntregaId = await db.ParadasEntrega.Where(p => p.Id == stop && p.ClienteAppId == a.TenantId).Select(p => p.OrdenEntregaId).SingleAsync(),
                            ReglaId = rule.Id, ReglaVersion = rule.Version, ActorId = a.UserId, CreatedAt = Now, UpdatedAt = Now });
                    }
                    settlement.Servicios += basis; settlement.Incentivos += incentive; settlement.Comisiones += commission;
                    settlement.Entregas += source.Paradas.Length;
                }
                // Cash belongs to the company and is received separately, never netted
                // from wages merely because a customer delivery was confirmed.
                settlement.EfectivoCompensado = 0;
            }
            else
            {
                var shop = r.TiendaId!.Value;
                if (!await db.Tiendas.AnyAsync(t => t.Id == shop && t.ClienteAppId == a.TenantId)) throw new OperationsException(404, "Comercio no disponible.");
                var orders = await db.OrdenesEntrega.AsNoTracking().Where(o => o.ClienteAppId == a.TenantId && o.TiendaId == shop && o.Estado == EstadoOrden.Entregada && o.EntregadaAt >= from && o.EntregadaAt < until).Include(o => o.Paradas).ToListAsync();
                foreach (var o in orders)
                {
                    var key = $"comercio:{shop}:servicio:{o.Id}";
                    if (await db.MovimientosOperativos.AnyAsync(m => m.ClienteAppId == a.TenantId && m.ClaveOrigen == key)) continue;
                    if (!o.Precio.HasValue) throw new OperationsException(400, "Una orden no tiene tarifa comercial confirmada.");
                    AddMovement(a, settlement, "Servicios", -o.Precio.Value, $"Servicio orden {o.CodigoA ?? o.NumeroOrdenA.ToString()}", key, new("Servicio", o.Id, o.Precio, [], o.Id, null), null);
                    settlement.Servicios -= o.Precio.Value; settlement.Entregas += o.Paradas.Count;
                }
                var cashQuery = db.CobrosOperativos.Where(c => c.ClienteAppId == a.TenantId && c.TiendaId == shop
                    && c.EntregadoEmpresaAt >= from && c.EntregadoEmpresaAt < until && c.LiquidacionId == null
                    && c.ImporteEntregadoEmpresa != null);
                // null/lista vacía mantiene el contrato anterior: todos los recibos
                // elegibles del comercio y período. Una lista no vacía exige todos.
                if (selectedCash is not null) cashQuery = cashQuery.Where(c => selectedCash.Contains(c.Id));
                var cash = await cashQuery.ToListAsync();
                if (selectedCash is not null && cash.Count != selectedCash.Length)
                    throw new OperationsException(409, "Uno de los cobros seleccionados no está disponible para este comercio y período. Actualiza la selección.");
                foreach (var c in cash)
                {
                    AddMovement(a, settlement, "CobrosComercio", c.ImporteEntregadoEmpresa!.Value, "Efectivo recibido por cuenta del comercio: " + c.Referencia,
                        $"comercio:{shop}:cobro:{c.Id}", new("Cobro", c.Id, null, [], c.OrdenEntregaId, null), null);
                    settlement.EfectivoCompensado -= c.ImporteEntregadoEmpresa.Value;
                    cashApplied.Add(c);
                }
                if (settlement.Movimientos.Count == 0) throw new OperationsException(400, "No hay servicios ni efectivo recibido pendientes de liquidar.");
            }
            settlement.Neto = Money(settlement.Servicios + settlement.Incentivos - settlement.Comisiones + settlement.Ajustes - settlement.EfectivoCompensado);
            await db.SaveChangesAsync();
            foreach (var cash in cashApplied) cash.LiquidacionId = settlement.Id;
            Audit(a, "Liquidacion", settlement.Id, "Liquidación generada con reglas y operaciones congeladas; efectivo de pilotos separado.");
            return new { id = settlement.Id, version = settlement.Version, neto = settlement.Neto };
        });
    }

    private void AddMovement(OperationsActor a, LiquidacionOperativa settlement, string concept, decimal amount, string description, string key, PaySource source, ReglaRemuneracionOperativa? rule)
        => settlement.Movimientos.Add(new() { ClienteAppId = a.TenantId, Concepto = concept, Importe = Money(amount), Descripcion = description,
            ClaveOrigen = key, OrdenEntregaId = source.OrdenId, RutaId = source.RutaId,
            ParadaEntregaId = source.Unidad == "Parada" ? source.Id : null, ReglaId = rule?.Id, ReglaVersion = rule?.Version,
            BasesJson = JsonSerializer.Serialize(new { source.Unidad, source.Id, source.Tarifa, source.Paradas, regla = rule }, Json), ActorId = a.UserId, CreatedAt = Now, UpdatedAt = Now });
    private sealed record PaySource(string Unidad, int Id, decimal? Tarifa, int[] Paradas, int? OrdenId, int? RutaId);

    public async Task<object> AjustarLiquidacionAsync(OperationsActor a, int id, AjustarLiquidacionRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "AjustarLiquidacion", r.OperacionId, new { id, r }, async () =>
        {
            var l = await SettlementAsync(a, id); CheckVersion(l, r.Version);
            if (l.Estado != EstadoLiquidacion.EnRevision) throw new OperationsException(400, "Los ajustes se registran antes de aprobar. No se reescribe una liquidación aprobada.");
            Amount(r.Importe, true); if (r.Importe == 0) throw new OperationsException(400, "El ajuste no puede ser cero.");
            var reason = Required(r.Motivo, 200, "Motivo");
            l.Movimientos.Add(new() { ClienteAppId = a.TenantId, Concepto = "Ajustes", Importe = r.Importe, Descripcion = reason,
                ClaveOrigen = $"ajuste:{a.UserId}:{Guid.Parse(r.OperacionId):N}", ActorId = a.UserId, CreatedAt = Now, UpdatedAt = Now });
            l.Ajustes += r.Importe; l.Neto = Money(l.Servicios + l.Incentivos - l.Comisiones + l.Ajustes - l.EfectivoCompensado);
            Touch(l); Audit(a, "Liquidacion", id, "Ajuste registrado: " + reason); return new { id, version = l.Version, neto = l.Neto };
        });
    }

    public async Task<object> CambiarLiquidacionAsync(OperationsActor a, int id, CambiarLiquidacionRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "CambiarLiquidacion", r.OperacionId, new { id, r }, async () =>
        {
            var l = await SettlementAsync(a, id); CheckVersion(l, r.Version);
            switch (r.Accion)
            {
                case "Aprobar":
                    if (l.Estado != EstadoLiquidacion.EnRevision || !l.Conciliada) throw new OperationsException(400, "Revisa y concilia antes de aprobar.");
                    l.Estado = EstadoLiquidacion.Aprobada; l.AprobadaPor = a.UserId; break;
                case "Programar":
                    if (l.Estado is not (EstadoLiquidacion.Aprobada or EstadoLiquidacion.Programada) || r.Fecha is null || r.Fecha < Today) throw new OperationsException(400, "Selecciona una fecha válida para la liquidación aprobada.");
                    l.MetodoPago = Required(r.Metodo, 40, "Método"); l.CuentaUltimos4 = AccountSuffix(r.CuentaUltimos4); l.FechaProgramada = r.Fecha; l.Estado = EstadoLiquidacion.Programada; break;
                case "RegistrarPago":
                    if (l.Estado is not (EstadoLiquidacion.Aprobada or EstadoLiquidacion.Programada) || r.Fecha is null || r.Fecha > Today) throw new OperationsException(400, "El pago externo requiere liquidación aprobada y fecha real no futura.");
                    var reference = Required(r.Referencia, 120, "Referencia del pago externo");
                    var method = Required(r.Metodo, 40, "Método");
                    if (await db.LiquidacionesOperativas.AnyAsync(x => x.ClienteAppId == a.TenantId && x.Id != id && x.Estado == EstadoLiquidacion.Pagada && x.MetodoPago == method && x.ReferenciaPago == reference)) throw new OperationsException(409, "Ya existe un pago con ese método y referencia. Revisa la conciliación.");
                    l.MetodoPago = method; l.CuentaUltimos4 = AccountSuffix(r.CuentaUltimos4); l.ReferenciaPago = reference;
                    l.PagadaAt = Now; l.FechaPago = r.Fecha; l.Estado = EstadoLiquidacion.Pagada; break;
                case "Conciliar":
                    if (l.Estado != EstadoLiquidacion.EnRevision) throw new OperationsException(400, "La liquidación aprobada conserva su conciliación.");
                    Required(r.Nota, 1000, "Nota de conciliación"); l.Conciliada = true; break;
                default: throw new OperationsException(400, "Acción financiera no válida.");
            }
            Touch(l); Audit(a, "Liquidacion", id, r.Accion + (string.IsNullOrWhiteSpace(r.Nota) ? "" : ": " + r.Nota.Trim()));
            return new { id, version = l.Version, estado = l.Estado.ToString() };
        });
    }
    private static string? AccountSuffix(string? value)
    { if (string.IsNullOrWhiteSpace(value)) return null; if (value.Length != 4 || value.Any(c => c is < '0' or > '9')) throw new OperationsException(400, "Indica solamente los cuatro últimos dígitos de la cuenta."); return value; }
    private async Task<LiquidacionOperativa> SettlementAsync(OperationsActor a, int id)
        => await db.LiquidacionesOperativas.Include(l => l.Movimientos).SingleOrDefaultAsync(l => l.Id == id && l.ClienteAppId == a.TenantId)
            ?? throw new OperationsException(404, "Liquidación no disponible.");
}
