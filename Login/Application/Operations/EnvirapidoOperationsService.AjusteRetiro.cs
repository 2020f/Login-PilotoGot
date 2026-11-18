using System.Text.Json;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed partial class EnvirapidoOperationsService
{
    public async Task<object> RegistrarAjusteRetiroAsync(OperationsActor a, int retiroId, RegistrarAjusteRetiroRequest r)
    {
        await EnsureActorAsync(a, true);
        return await MutateAsync(a, "AjusteRetiro", r.OperacionId, new { retiroId, r }, async () =>
        {
            var retiro = await LoadRetiroAsync(a, retiroId);
            CheckVersion(retiro, r.Version);
            if (retiro.Estado is "Solicitada" or "Rechazada" || !retiro.PilotoOrigenId.HasValue)
                throw new OperationsException(409, "El ajuste requiere un retiro autorizado con piloto de origen.");
            var pilotId = retiro.PilotoOrigenId.Value;
            if (!await db.Pilotos.AnyAsync(p => p.Id == pilotId && p.ClienteAppId == a.TenantId))
                throw new OperationsException(404, "El piloto de origen no está disponible en esta empresa.");
            Amount(r.Importe);
            if (r.Importe == 0) throw new OperationsException(400, "El ajuste debe ser mayor que cero.");
            var reason = Required(r.Motivo, 200, "Motivo del ajuste");
            var sourceKey = $"retiro:{retiroId}:ajuste";
            if (await db.MovimientosOperativos.AnyAsync(m => m.ClienteAppId == a.TenantId && m.ClaveOrigen == sourceKey))
                throw new OperationsException(409, "Este retiro ya tiene un ajuste. Corrige la liquidación existente antes de aprobarla.");
            // A manual compensation records the manager's amount, not an invented
            // completed delivery or a tariff allocation. Cash remains separate.
            var settlement = new LiquidacionOperativa
            {
                ClienteAppId = a.TenantId, PilotoId = pilotId, Desde = Today, Hasta = Today,
                Estado = EstadoLiquidacion.EnRevision, Ajustes = r.Importe, Neto = r.Importe,
                CreatedAt = Now, UpdatedAt = Now
            };
            settlement.Movimientos.Add(new MovimientoOperativo
            {
                ClienteAppId = a.TenantId, Concepto = "Ajustes", Importe = r.Importe, Descripcion = reason,
                ClaveOrigen = sourceKey, OrdenEntregaId = retiro.OrdenEntregaId, ParadaEntregaId = retiro.ParadaEntregaId,
                ActorId = a.UserId, CreatedAt = Now, UpdatedAt = Now,
                BasesJson = JsonSerializer.Serialize(new { unidad = "AjusteManualRetiro", retiroId, pilotoOrigenId = pilotId,
                    estadoRetiro = retiro.Estado, retiro.CreatedAt, importe = r.Importe, motivo = reason }, Json)
            });
            db.LiquidacionesOperativas.Add(settlement);
            Touch(retiro);
            await db.SaveChangesAsync();
            Audit(a, "Liquidacion", settlement.Id, $"Ajuste manual del retiro {retiroId}, sin entregas ni efectivo compensado. {reason}");
            return new { id = settlement.Id, version = settlement.Version, neto = settlement.Neto,
                retiroId, retiroVersion = retiro.Version };
        });
    }
}
