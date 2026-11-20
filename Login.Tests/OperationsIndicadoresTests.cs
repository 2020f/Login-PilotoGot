using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class OperationsIndicadoresTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")).DateTime);
    private static DateTime LocalAt(DateOnly day, int hour = 12) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo"));
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Totals_exceed_list_caps_keep_decimal_precision_and_remain_scoped_to_tenant_and_pilot()
    {
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>(); var orders = sp.GetRequiredService<IOrdenService>();
            var order = await orders.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
            await orders.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            var foreign = await orders.CrearOrdenAsync(app.TenantB, app.ShopB, app.CustomerB, operacionId: Key());
            db.IncidenciasOperativas.AddRange(Enumerable.Range(0, 1005).Select(_ => new IncidenciaOperativa {
                ClienteAppId = app.TenantA, OrdenEntregaId = order.OrdenId, Estado = EstadoIncidencia.Nueva,
                Prioridad = 4, VenceAt = app.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1), CreatedAt = LocalAt(Today), ReportadaPor = "manager-a" }));
            db.IncidenciasOperativas.Add(new() { ClienteAppId = app.TenantB, OrdenEntregaId = foreign.OrdenId, Prioridad = 4 });
            db.LiquidacionesOperativas.AddRange(Enumerable.Range(0, 505).Select(_ => new LiquidacionOperativa {
                ClienteAppId = app.TenantA, PilotoId = app.PilotA, Desde = Today, Hasta = Today, Neto = .01m, Estado = EstadoLiquidacion.EnRevision }));
            db.LiquidacionesOperativas.Add(new() { ClienteAppId = app.TenantA, PilotoId = app.PilotA2, Desde = Today, Hasta = Today, Neto = 23.45m, Estado = EstadoLiquidacion.Pagada });
            db.LiquidacionesOperativas.Add(new() { ClienteAppId = app.TenantA, TiendaId = app.ShopA, Desde = Today, Hasta = Today, Neto = 77m, Estado = EstadoLiquidacion.EnRevision });
            db.LiquidacionesOperativas.Add(new() { ClienteAppId = app.TenantB, PilotoId = app.PilotB, Desde = Today, Hasta = Today, Neto = 9999m });
            await db.SaveChangesAsync();
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var all = await service.IndicadoresAsync(await service.ManagerAsync("manager-a"));
            Assert.Equal(1005, all.IncidenciasAbiertas); Assert.Equal(1005, all.IncidenciasPorEstado["Nueva"]);
            Assert.Equal(1005, all.IncidenciasCriticas); Assert.Equal(1005, all.IncidenciasSinResponsable); Assert.Equal(1005, all.IncidenciasFueraPlazo);
            Assert.Equal(507, all.TotalLiquidaciones); Assert.Equal(105.50m, all.NetoPeriodo);
            Assert.Equal(new ResumenLiquidacionesEstado(506, 82.05m), all.LiquidacionesPorEstado["EnRevision"]);
            Assert.Equal(new ResumenLiquidacionesEstado(1, 23.45m), all.LiquidacionesPorEstado["Pagada"]);
            Assert.Equal(new ResumenLiquidacionesEstado(505, 5.05m), all.LiquidacionesPilotosPorEstado["EnRevision"]);
            Assert.Equal(new ResumenLiquidacionesEstado(1, 77m), all.LiquidacionesComerciosPorEstado["EnRevision"]);
            var firstPilot = await service.IndicadoresAsync(await service.PilotAsync("pilot-a"));
            Assert.Equal(5.05m, firstPilot.NetoPeriodo); Assert.Equal(505, firstPilot.TotalLiquidaciones); Assert.Equal(1005, firstPilot.IncidenciasAbiertas);
            Assert.Equal(0, firstPilot.LiquidacionesComerciosPorEstado.Sum(x => x.Value.Cantidad));
            var secondPilot = await service.IndicadoresAsync(await service.PilotAsync("pilot-a2"));
            Assert.Equal(23.45m, secondPilot.NetoPeriodo); Assert.Equal(1, secondPilot.TotalLiquidaciones); Assert.Equal(0, secondPilot.IncidenciasAbiertas);
            var otherTenantPilot = await service.IndicadoresAsync(await service.PilotAsync("pilot-b"));
            Assert.Equal(9999m, otherTenantPilot.NetoPeriodo); Assert.Equal(1, otherTenantPilot.TotalLiquidaciones);
            Assert.Equal(403, (await Assert.ThrowsAsync<OperationsException>(() => service.IndicadoresAsync(new(app.TenantB, "manager-a")))).StatusCode);
        });
    }

    [Fact]
    public async Task Period_uses_local_incident_dates_route_dates_and_financial_overlap_but_current_load_and_resolutions_today()
    {
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>(); var core = sp.GetRequiredService<IOrdenService>();
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            var yesterday = Today.AddDays(-1);
            var old = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key(),
                destinatarios: [new(app.CustomerA, null), new(app.CustomerA, null)]);
            var current = await core.CrearOrdenAsync(app.TenantA, app.ShopA2, app.CustomerA2, operacionId: Key());
            var future = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
            var polygon = new List<GeoPoint> { new(18.4, -69.99), new(18.4, -69.9), new(18.5, -69.9) };
            var activeZone = Ack(await service.GuardarZonaAsync(manager, new("Zona vigente", "QA", "#008b89", polygon, 1, "ParadasPendientes", 80, "07:00", "22:00", true, Key()))).GetProperty("id").GetInt32();
            var futureZone = Ack(await service.GuardarZonaAsync(manager, new("Zona sólo futuro", "QA", "#008b89", polygon, 1, "ParadasPendientes", 80, "07:00", "22:00", true, Key()))).GetProperty("id").GetInt32();
            foreach (var tuple in new[] { (Order: old, Date: yesterday, Pilot: app.PilotA), (Order: current, Date: Today, Pilot: app.PilotA2) })
            {
                await service.GuardarDatosEnvioAsync(manager, tuple.Order.OrdenId, new(null, "Estandar", 1, null, 2, activeZone, null, null, null, Key()));
                var route = Ack(await service.CrearRutaAsync(manager, new("Ruta " + tuple.Order.OrdenId, tuple.Date, [tuple.Order.OrdenId], tuple.Pilot, activeZone, false, Key())));
                await service.EstadoRutaAsync(manager, route.GetProperty("id").GetInt32(), new("Iniciar", route.GetProperty("version").GetString()!, Key()));
            }
            await service.GuardarDatosEnvioAsync(manager, future.OrdenId, new(null, "Programado", 1, null, 2, futureZone,
                app.Clock.GetUtcNow().UtcDateTime.AddDays(1), null, null, Key()));
            db.IncidenciasOperativas.AddRange(
                new IncidenciaOperativa { ClienteAppId = app.TenantA, OrdenEntregaId = old.OrdenId, CreatedAt = LocalAt(yesterday), Estado = EstadoIncidencia.Resuelta, ResueltaAt = LocalAt(Today, 0) },
                new IncidenciaOperativa { ClienteAppId = app.TenantA, OrdenEntregaId = current.OrdenId, CreatedAt = LocalAt(Today, 0), Estado = EstadoIncidencia.Escalada, Prioridad = 4 });
            db.LiquidacionesOperativas.AddRange(
                new LiquidacionOperativa { ClienteAppId = app.TenantA, PilotoId = app.PilotA, Desde = yesterday, Hasta = yesterday, Neto = 2.20m, Estado = EstadoLiquidacion.Pagada },
                new LiquidacionOperativa { ClienteAppId = app.TenantA, PilotoId = app.PilotA2, Desde = Today, Hasta = Today, Neto = 1.10m, Estado = EstadoLiquidacion.EnRevision },
                new LiquidacionOperativa { ClienteAppId = app.TenantA, PilotoId = app.PilotA, Desde = yesterday, Hasta = Today, Neto = 3.30m, Estado = EstadoLiquidacion.Programada });
            await db.SaveChangesAsync();
            var currentMetrics = await service.IndicadoresAsync(manager, Today, Today);
            var historical = await service.IndicadoresAsync(manager, yesterday, yesterday);
            Assert.Equal(1, currentMetrics.RutasEnCurso); Assert.Equal(1, currentMetrics.ParadasPendientes);
            Assert.Equal(1, historical.RutasEnCurso); Assert.Equal(2, historical.ParadasPendientes);
            Assert.Equal(1, currentMetrics.IncidenciasPorEstado["Escalada"]); Assert.Equal(0, currentMetrics.IncidenciasPorEstado["Resuelta"]);
            Assert.Equal(1, historical.IncidenciasPorEstado["Resuelta"]); Assert.Equal(0, historical.IncidenciasAbiertas);
            Assert.Equal(1, currentMetrics.IncidenciasResueltasHoy); Assert.Equal(1, historical.IncidenciasResueltasHoy);
            Assert.Equal(2, currentMetrics.ZonasActivas); Assert.Equal(1, currentMetrics.ZonasAltaCarga); Assert.Equal(1, historical.ZonasAltaCarga);
            Assert.Equal(4.40m, currentMetrics.NetoPeriodo); Assert.Equal(5.50m, historical.NetoPeriodo);
            Assert.Equal(2, currentMetrics.TotalLiquidaciones); Assert.Equal(2, historical.TotalLiquidaciones);
            var own = await service.IndicadoresAsync(await service.PilotAsync("pilot-a2"), Today, Today);
            Assert.Equal(1, own.ParadasPendientes); Assert.Equal(1.10m, own.NetoPeriodo);
            Assert.Equal(400, (await Assert.ThrowsAsync<OperationsException>(() => service.IndicadoresAsync(manager, Today, yesterday))).StatusCode);
        });
    }
}
