using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class OperationsIncidenciaRutaTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")).DateTime);
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Omitted_route_is_captured_for_pilot_and_manager_only_when_current_assignment_matches_and_replay_keeps_original_case()
    {
        await app.InScopeAsync(async sp => {
            var core = sp.GetRequiredService<IOrdenService>(); var db = sp.GetRequiredService<ApplicationDbContext>();
            var service = sp.GetRequiredService<EnvirapidoOperationsService>(); var manager = await service.ManagerAsync("manager-a");
            var order = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
            var route = Ack(await service.CrearRutaAsync(manager, new("Contexto de incidencia QA", Today, [order.OrdenId], app.PilotA, null, false, Key())));
            var routeId = route.GetProperty("id").GetInt32();
            var active = Ack(await service.EstadoRutaAsync(manager, routeId, new("Iniciar", route.GetProperty("version").GetString()!, Key())));
            var pilot = await service.PilotAsync("pilot-a");
            var request = new ReportarIncidenciaRequest("Sin contacto", "Reporte desde detalle de pedido", Key(), order.OrdenId, order.Paradas[0].ParadaId);
            var first = Ack(await service.ReportarIncidenciaAsync(pilot, request));
            var firstId = first.GetProperty("id").GetInt32();
            Assert.Equal(routeId, (await db.IncidenciasOperativas.AsNoTracking().SingleAsync(i => i.Id == firstId)).RutaId);
            Assert.Equal(routeId, Assert.Single((await service.SnapshotAsync(pilot)).Incidencias).RutaId);
            var managerCase = Ack(await service.ReportarIncidenciaAsync(manager, request with { OperacionId = Key(), Descripcion = "Reporte del gestor" }));
            Assert.Equal(routeId, (await db.IncidenciasOperativas.AsNoTracking().SingleAsync(i => i.Id == managerCase.GetProperty("id").GetInt32())).RutaId);
            // Simular datos previos inconsistentes: una asignación distinta nunca
            // autoriza inferir el recorrido del custodio anterior.
            await db.OrdenesEntrega.Where(o => o.Id == order.OrdenId).ExecuteUpdateAsync(x => x.SetProperty(o => o.PilotoId, app.PilotA2));
            db.ChangeTracker.Clear();
            var mismatch = Ack(await service.ReportarIncidenciaAsync(await service.PilotAsync("pilot-a2"), request with { OperacionId = Key() }));
            Assert.Null((await db.IncidenciasOperativas.AsNoTracking().SingleAsync(i => i.Id == mismatch.GetProperty("id").GetInt32())).RutaId);
            await db.OrdenesEntrega.Where(o => o.Id == order.OrdenId).ExecuteUpdateAsync(x => x.SetProperty(o => o.PilotoId, app.PilotA));
            db.ChangeTracker.Clear();
            await service.EstadoRutaAsync(manager, routeId, new("Cancelar", active.GetProperty("version").GetString()!, Key()));
            var replay = Ack(await service.ReportarIncidenciaAsync(pilot, request));
            Assert.Equal(first.GetRawText(), replay.GetRawText());
            Assert.Equal(routeId, (await db.IncidenciasOperativas.AsNoTracking().SingleAsync(i => i.Id == firstId)).RutaId);
            Assert.Equal(3, await db.IncidenciasOperativas.CountAsync());
        });
    }

    [Fact]
    public async Task Historical_or_planned_routes_are_not_inferred_and_foreign_order_or_route_is_rejected()
    {
        await app.InScopeAsync(async sp => {
            var core = sp.GetRequiredService<IOrdenService>(); var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var db = sp.GetRequiredService<ApplicationDbContext>(); var manager = await service.ManagerAsync("manager-a");
            var historicalOrder = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
            var historicalRoute = Ack(await service.CrearRutaAsync(manager, new("Histórica QA", Today, [historicalOrder.OrdenId], app.PilotA, null, false, Key())));
            var historyId = historicalRoute.GetProperty("id").GetInt32();
            await service.EstadoRutaAsync(manager, historyId, new("Iniciar", historicalRoute.GetProperty("version").GetString()!, Key()));
            await core.ConfirmarRecolectaAsync(historicalOrder.CodigoB, "pilot-a", "Piloto", historicalOrder.OrdenId, Key());
            await core.ConfirmarEntregaParadaAsync(historicalOrder.OrdenId, historicalOrder.Paradas[0].ParadaId, historicalOrder.Paradas[0].CodigoC, "pilot-a", Key());
            Assert.Equal(EstadoRutaOperativa.Finalizada, (await db.RutasOperativas.AsNoTracking().SingleAsync(r => r.Id == historyId)).Estado);
            var historyCase = Ack(await service.ReportarIncidenciaAsync(await service.PilotAsync("pilot-a"), new("General", "Reporte posterior a la entrega", Key(), historicalOrder.OrdenId)));
            Assert.Null((await db.IncidenciasOperativas.AsNoTracking().SingleAsync(i => i.Id == historyCase.GetProperty("id").GetInt32())).RutaId);
            var plannedOrder = await core.CrearOrdenAsync(app.TenantA, app.ShopA2, app.CustomerA2, operacionId: Key());
            await service.CrearRutaAsync(manager, new("Planificada QA", Today, [plannedOrder.OrdenId], app.PilotA2, null, false, Key()));
            var plannedCase = Ack(await service.ReportarIncidenciaAsync(manager, new("General", "Preparación del comercio", Key(), plannedOrder.OrdenId)));
            Assert.Null((await db.IncidenciasOperativas.AsNoTracking().SingleAsync(i => i.Id == plannedCase.GetProperty("id").GetInt32())).RutaId);
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.CreateAsync(new IdentityUser { Id = "manager-b", UserName = "manager-b@example.test", Email = "manager-b@example.test" }, PilotGoFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync("manager-b"))!, "Gestor")).Succeeded);
            (await db.ClientesApp.FindAsync(app.TenantB))!.GestorIdentityUserId = "manager-b"; await db.SaveChangesAsync();
            var foreign = await core.CrearOrdenAsync(app.TenantB, app.ShopB, app.CustomerB, operacionId: Key());
            var managerB = await service.ManagerAsync("manager-b");
            var foreignRoute = Ack(await service.CrearRutaAsync(managerB, new("Empresa B QA", Today, [foreign.OrdenId], app.PilotB, null, false, Key())));
            var foreignRouteId = foreignRoute.GetProperty("id").GetInt32();
            await service.EstadoRutaAsync(managerB, foreignRouteId, new("Iniciar", foreignRoute.GetProperty("version").GetString()!, Key()));
            Assert.Equal(404, (await Assert.ThrowsAsync<OperationsException>(() => service.ReportarIncidenciaAsync(manager,
                new("General", "No pertenece a esta empresa", Key(), foreign.OrdenId)))).StatusCode);
            Assert.Equal(404, (await Assert.ThrowsAsync<OperationsException>(() => service.ReportarIncidenciaAsync(manager,
                new("General", "Recorrido de empresa ajena", Key(), plannedOrder.OrdenId, RutaId: foreignRouteId)))).StatusCode);
            Assert.Equal(2, await db.IncidenciasOperativas.CountAsync());
        });
    }
}
