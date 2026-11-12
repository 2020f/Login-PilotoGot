using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    public async Task<OperationsActivity> ActividadAsync(OperationsActor a)
    {
        if (!a.EsPiloto) throw new OperationsException(403, "La actividad pertenece al piloto autenticado.");
        await EnsureActorAsync(a);
        var active = await db.OrdenesEntrega.AnyAsync(o => o.ClienteAppId == a.TenantId && o.PilotoId == a.PilotoId
            && !EstadosEntrega.Terminales.Contains(o.Estado) && (o.Estado == EstadoOrden.Asignada || o.RecolectadaAt != null));
        var lease = Math.Clamp(configuration.GetValue<int?>("Envirapido:GpsAutorizacionSegundos") ?? 120, 60, 900);
        return new(Now, active, active ? Now.AddSeconds(lease) : null, lease);
    }

    public async Task<object> GuardarDatosEnvioAsync(OperationsActor a, int orderId, GuardarDatosEnvioRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "DatosEnvio", r.OperacionId, new { orderId, r }, async () =>
        {
            var order = await OrderAsync(a, orderId);
            var data = await db.DatosEnviosOperativos.SingleOrDefaultAsync(d => d.ClienteAppId == a.TenantId && d.OrdenEntregaId == orderId);
            if (r.Paquetes is < 1 or > 10000 || r.PesoKg is < 0 or > 100000 || r.Prioridad is < 1 or > 4) throw new OperationsException(400, "Paquetes, peso o prioridad fuera del intervalo permitido.");
            if (r.PesoKg.HasValue) Amount(r.PesoKg.Value);
            if (r.ZonaId.HasValue && !await db.ZonasOperativas.AnyAsync(z => z.Id == r.ZonaId && z.ClienteAppId == a.TenantId && z.Activa)) throw new OperationsException(400, "Zona no disponible.");
            if (r.ProgramadaPara.HasValue && r.ProgramadaPara.Value.Kind == DateTimeKind.Unspecified) throw new OperationsException(400, "La programación debe incluir zona horaria.");
            if (order.Estado != EstadoOrden.Creada && r.ProgramadaPara?.ToUniversalTime() != data?.ProgramadaPara) throw new OperationsException(400, "Solo se modifica la programación de una orden sin asignar.");
            if (r.CompromisoEntrega.HasValue && r.CompromisoEntrega.Value.Kind == DateTimeKind.Unspecified) throw new OperationsException(400, "El compromiso de entrega debe incluir zona horaria.");
            if (data is null) { data = new() { ClienteAppId = a.TenantId, OrdenEntregaId = orderId, CreatedAt = Now }; db.DatosEnviosOperativos.Add(data); }
            else CheckVersion(data, r.Version);
            data.Referencia = Optional(r.Referencia, 100, "Referencia"); data.Servicio = Required(r.Servicio, 60, "Servicio");
            data.Paquetes = r.Paquetes; data.PesoKg = r.PesoKg; data.Prioridad = r.Prioridad; data.ZonaId = r.ZonaId;
            data.ProgramadaPara = r.ProgramadaPara?.ToUniversalTime(); data.CompromisoEntrega = r.CompromisoEntrega?.ToUniversalTime();
            data.NotasOperativas = Optional(r.NotasOperativas, 1000, "Notas");
            if (r.PrecioComercial.HasValue && r.PrecioComercial != order.Precio)
            {
                Amount(r.PrecioComercial.Value);
                if (await db.MovimientosOperativos.AnyAsync(m => m.ClienteAppId == a.TenantId && m.OrdenEntregaId == orderId)) throw new OperationsException(409, "La tarifa ya forma parte de una liquidación; registra un ajuste financiero en lugar de cambiar su base.");
                order.Precio = r.PrecioComercial;
            }
            Touch(data); await db.SaveChangesAsync(); Audit(a, "Orden", orderId, "Datos operativos y compromiso actualizados.");
            return new { id = data.Id, version = data.Version };
        });
    }

    public async Task<object> RegistrarCobroAsync(OperationsActor a, RegistrarCobroRequest r)
        => await MutateAsync(a, "RegistrarCobro", r.OperacionId, r, async () =>
        {
            Amount(r.Importe); if (r.Importe == 0) throw new OperationsException(400, "El cobro debe ser mayor que cero.");
            var order = await OrderAsync(a, r.OrdenId);
            if (order.PilotoId is null || !order.RecolectadaAt.HasValue) throw new OperationsException(400, "El cobro debe corresponder a una orden recogida y a su piloto.");
            if (r.ParadaId.HasValue && !order.Paradas.Any(p => p.Id == r.ParadaId)) throw new OperationsException(404, "Parada no disponible.");
            var reference = Required(r.Referencia, 80, "Referencia");
            if (await db.CobrosOperativos.AnyAsync(c => c.ClienteAppId == a.TenantId && c.Referencia == reference)) throw new OperationsException(409, "Ya existe un cobro con esa referencia.");
            var cash = new CobroOperativo { ClienteAppId = a.TenantId, OrdenEntregaId = order.Id, ParadaEntregaId = r.ParadaId,
                PilotoId = order.PilotoId.Value, TiendaId = order.TiendaId, Importe = r.Importe, Referencia = reference,
                Nota = Optional(r.Nota, 500, "Nota"), CreatedAt = Now, UpdatedAt = Now };
            db.CobrosOperativos.Add(cash); await db.SaveChangesAsync(); Audit(a, "Cobro", cash.Id, "Cobro declarado por cuenta del comercio; pendiente de verificación y entrega a la empresa.");
            return new { id = cash.Id, version = cash.Version };
        });

    public async Task<object> ConciliarCobroAsync(OperationsActor a, int id, ConciliarCobroRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "ConciliarCobro", r.OperacionId, new { id, r }, async () =>
        {
            Amount(r.ImporteVerificado);
            var cash = await db.CobrosOperativos.SingleOrDefaultAsync(c => c.Id == id && c.ClienteAppId == a.TenantId) ?? throw new OperationsException(404, "Cobro no disponible.");
            CheckVersion(cash, r.Version);
            if (cash.EntregadoEmpresaAt.HasValue || cash.LiquidacionId.HasValue) throw new OperationsException(400, "El efectivo ya recibido o liquidado conserva su verificación histórica.");
            cash.ImporteVerificado = r.ImporteVerificado; cash.Nota = Required(r.Nota, 500, "Motivo de conciliación"); cash.ConciliadoAt = Now;
            Touch(cash); Audit(a, "Cobro", id, "Importe verificado; cualquier diferencia se conserva frente al importe declarado.");
            return new { id, version = cash.Version, diferencia = cash.Importe - cash.ImporteVerificado };
        });
    }

    public async Task<object> RecibirEfectivoAsync(OperationsActor a, int id, RecibirEfectivoRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "RecibirEfectivo", r.OperacionId, new { id, r }, async () =>
        {
            Amount(r.Importe);
            var cash = await db.CobrosOperativos.SingleOrDefaultAsync(c => c.Id == id && c.ClienteAppId == a.TenantId) ?? throw new OperationsException(404, "Cobro no disponible.");
            CheckVersion(cash, r.Version);
            if (cash.EntregadoEmpresaAt.HasValue || cash.ImporteVerificado is null || cash.ConciliadoAt is null) throw new OperationsException(400, "Verifica el importe y registra una sola recepción de efectivo.");
            if (r.Importe != cash.ImporteVerificado) throw new OperationsException(400, "La recepción debe coincidir con el importe verificado. Atiende antes cualquier diferencia.");
            var reference = Required(r.Referencia, 120, "Referencia de recepción");
            if (await db.CobrosOperativos.AnyAsync(c => c.ClienteAppId == a.TenantId && c.Id != id && c.ReferenciaEntregaEmpresa == reference)) throw new OperationsException(409, "La referencia de recepción ya está registrada.");
            cash.EntregadoEmpresaAt = Now; cash.ImporteEntregadoEmpresa = r.Importe; cash.ReferenciaEntregaEmpresa = reference;
            cash.RecibidoEmpresaPor = a.UserId; Touch(cash);
            Audit(a, "Cobro", id, "Efectivo recibido por la empresa; no se descuenta de las ganancias del piloto.");
            return new { id, version = cash.Version, recibidoAt = cash.EntregadoEmpresaAt };
        });
    }

    public async Task<object> EnviarGpsAsync(OperationsActor a, EnviarGpsRequest r)
    {
        if (!a.EsPiloto) throw new OperationsException(403, "La ubicación pertenece al piloto autenticado.");
        return await MutateAsync(a, "Gps", r.OperacionId, r, async () =>
        {
            if (!Guid.TryParse(r.DispositivoId, out var device) || r.Muestras is null || r.Muestras.Count is < 1 or > 100) throw new OperationsException(400, "El lote de ubicación no es válido.");
            if (!await db.OrdenesEntrega.AnyAsync(o => o.ClienteAppId == a.TenantId && o.PilotoId == a.PilotoId && !EstadosEntrega.Terminales.Contains(o.Estado)
                    && (o.Estado == EstadoOrden.Asignada || o.RecolectadaAt != null))) throw new OperationsException(403, "No hay una entrega activa que autorice el seguimiento.");
            var deviceId = device.ToString("N"); var accepted = 0;
            var pilot = await db.Pilotos.SingleAsync(p => p.Id == a.PilotoId && p.ClienteAppId == a.TenantId);
            if (r.Muestras.Select(m => m.Secuencia).Distinct().Count() != r.Muestras.Count) throw new OperationsException(400, "El lote contiene secuencias repetidas.");
            foreach (var sample in r.Muestras.OrderBy(m => m.CapturadoAt))
            {
                var captured = sample.CapturadoAt.ToUniversalTime();
                if (!GeoGeometry.Valid(new(sample.Latitud, sample.Longitud)) || sample.Secuencia < 0 || sample.CapturadoAt.Kind == DateTimeKind.Unspecified || captured < Now.AddHours(-24) || captured > Now.AddMinutes(2) || sample.Precision is < 0 or > 100000 || (sample.Precision.HasValue && !double.IsFinite(sample.Precision.Value))) throw new OperationsException(400, "Una muestra de ubicación contiene coordenadas, precisión, fecha o secuencia no válidas.");
                var prior = await db.PosicionesPilotosOperativas.AsNoTracking().SingleOrDefaultAsync(p => p.ClienteAppId == a.TenantId && p.PilotoId == a.PilotoId && p.DispositivoId == deviceId && p.Secuencia == sample.Secuencia);
                if (prior is not null)
                {
                    if (prior.Latitud != sample.Latitud || prior.Longitud != sample.Longitud || DateTime.SpecifyKind(prior.CapturadoAt, DateTimeKind.Utc) != captured) throw new OperationsException(409, "Una secuencia de ubicación ya existe con otros datos.");
                    continue;
                }
                db.PosicionesPilotosOperativas.Add(new() { ClienteAppId = a.TenantId, PilotoId = pilot.Id,
                    DispositivoId = deviceId, Secuencia = sample.Secuencia, CapturadoAt = captured, RecibidoAt = Now,
                    Latitud = sample.Latitud, Longitud = sample.Longitud, Precision = sample.Precision }); accepted++;
                if (pilot.UltimaUbicacionAt is null || captured > DateTime.SpecifyKind(pilot.UltimaUbicacionAt.Value, DateTimeKind.Utc))
                { pilot.Latitud = sample.Latitud; pilot.Longitud = sample.Longitud; pilot.UltimaUbicacionAt = captured; }
            }
            return new { ok = true, recibidas = accepted, ultimaUbicacionAt = pilot.UltimaUbicacionAt };
        });
    }
}
