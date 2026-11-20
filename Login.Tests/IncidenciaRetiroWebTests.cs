using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class IncidenciaRetiroWebTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static string Key() => Guid.NewGuid().ToString("N");
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    private async Task<(OrdenCreadaResult Order, int Incident)> ScenarioAsync(bool foreign = false)
    {
        OrdenCreadaResult order = null!;
        var incident = 0;
        await app.InScopeAsync(async sp =>
        {
            var core = sp.GetRequiredService<IOrdenService>();
            var tenant = foreign ? app.TenantB : app.TenantA;
            var pilot = foreign ? app.PilotB : app.PilotA;
            var actor = foreign ? "pilot-b" : "pilot-a";
            var customer = foreign ? app.CustomerB : app.CustomerA;
            if (foreign)
            {
                var users = sp.GetRequiredService<UserManager<IdentityUser>>();
                var managerB = new IdentityUser { Id = "manager-b", UserName = "manager-b@example.test", Email = "manager-b@example.test" };
                Assert.True((await users.CreateAsync(managerB, PilotGoFactory.Password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(managerB, "Gestor")).Succeeded);
                var db = sp.GetRequiredService<ApplicationDbContext>();
                (await db.ClientesApp.SingleAsync(t => t.Id == app.TenantB)).GestorIdentityUserId = managerB.Id;
                await db.SaveChangesAsync();
            }
            order = await core.CrearOrdenAsync(tenant, foreign ? app.ShopB : app.ShopA, customer,
                operacionId: Key(), destinatarios: [new(customer), new(customer)]);
            await core.AsignarPilotoAsync(order.CodigoB, pilot, foreign ? "manager-b" : "manager-a", "Gestor", order.OrdenId);
            var result = await sp.GetRequiredService<EnvirapidoOperationsService>().ReportarIncidenciaAsync(
                new(tenant, actor, pilot), new("General", "Paquete pendiente de retiro por el gestor", Key(), order.OrdenId, order.Paradas[0].ParadaId));
            incident = JsonSerializer.SerializeToElement(result).GetProperty("id").GetInt32();
        });
        return (order, incident);
    }

    [Fact]
    public async Task Incident_exposes_contextual_retire_and_transfer_links_and_keeps_assignment_on_get()
    {
        var (order, incident) = await ScenarioAsync();
        using var manager = await app.LoginWebAsync("manager-a");
        var html = WebUtility.HtmlDecode(await manager.GetStringAsync($"/Operaciones/Incidencias?id={incident}"));
        Assert.Contains("Retirar del piloto", html);
        Assert.Contains("Reasignar", html);
        Assert.Contains($"incidenciaId={incident}", html);
        Assert.Contains($"ordenId={order.OrdenId}", html);
        Assert.Contains($"paradaId={order.Paradas[0].ParadaId}", html);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(app.PilotA, (await db.OrdenesEntrega.SingleAsync(o => o.Id == order.OrdenId)).PilotoId);
            Assert.False(await db.RetirosParadas.AnyAsync());
        });
    }

    [Fact]
    public async Task Contextual_management_limits_selection_to_affected_stop_without_mutating()
    {
        var (order, incident) = await ScenarioAsync();
        using var manager = await app.LoginWebAsync("manager-a");
        var html = await manager.GetStringAsync($"/Operaciones/Reasignaciones?ordenId={order.OrdenId}&incidenciaId={incident}&paradaId={order.Paradas[0].ParadaId}&accion=reasignar");
        Assert.Contains($"data-reassign-stop=\"{order.Paradas[0].ParadaId}\"", html);
        Assert.DoesNotContain($"data-reassign-stop=\"{order.Paradas[1].ParadaId}\"", html);
        Assert.DoesNotContain("name=\"codigoB\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"codigoC\"", html, StringComparison.OrdinalIgnoreCase);
        await app.InScopeAsync(async sp => Assert.False(await sp.GetRequiredService<ApplicationDbContext>().RetirosParadas.AnyAsync()));
    }

    [Fact]
    public async Task Contextual_management_rejects_foreign_incident_and_mismatched_stop()
    {
        var (order, incident) = await ScenarioAsync();
        var (_, foreignIncident) = await ScenarioAsync(foreign: true);
        using var manager = await app.LoginWebAsync("manager-a");
        using var foreign = await manager.GetAsync($"/Operaciones/Reasignaciones?ordenId={order.OrdenId}&incidenciaId={foreignIncident}&accion=retiros");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var sibling = await manager.GetAsync($"/Operaciones/Reasignaciones?ordenId={order.OrdenId}&incidenciaId={incident}&paradaId={order.Paradas[1].ParadaId}&accion=retiros");
        Assert.Equal(HttpStatusCode.NotFound, sibling.StatusCode);
    }

    [Fact]
    public async Task Default_management_lists_assigned_work_and_orders_expose_transfer_entry()
    {
        var (order, _) = await ScenarioAsync();
        using var manager = await app.LoginWebAsync("manager-a");
        var board = await manager.GetStringAsync("/Operaciones/Reasignaciones");
        Assert.Contains($"data-reassign-stop=\"{order.Paradas[0].ParadaId}\"", board);
        var listing = WebUtility.HtmlDecode(await manager.GetStringAsync("/Operaciones/Pedidos"));
        Assert.Contains($"/Operaciones/Reasignaciones?ordenId={order.OrdenId}", listing);
        Assert.Contains("Cambiar piloto", listing);
    }

    [Fact]
    public async Task Browser_csrf_retire_command_links_incident_without_pickup_or_delivery_codes()
    {
        var (order, incident) = await ScenarioAsync();
        using var manager = await app.LoginWebAsync("manager-a");
        var html = await manager.GetStringAsync($"/Operaciones/Reasignaciones?ordenId={order.OrdenId}&incidenciaId={incident}");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        manager.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(match.Groups[1].Value));
        string revision = "";
        await app.InScopeAsync(async sp => revision = await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega
            .Where(p => p.Id == order.Paradas[0].ParadaId).Select(p => p.RevisionAsignacion).SingleAsync());
        var request = new SolicitarRetiroParadasRequest(order.OrdenId,
            [new(order.Paradas[0].ParadaId, revision)], "Retiro autorizado desde incidencia", Key(), incident);
        using var response = await manager.PostAsJsonAsync("/api/operaciones/retiros", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<JsonElement>();
        var management = ack.GetProperty("gestiones")[0];
        Assert.Equal(incident, management.GetProperty("incidenciaId").GetInt32());
        Assert.Equal("PorReasignar", management.GetProperty("estado").GetString());
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var stop = await db.ParadasEntrega.SingleAsync(p => p.Id == order.Paradas[0].ParadaId);
            Assert.Null(stop.PilotoAsignadoId);
            Assert.True(stop.EnReasignacion);
            Assert.Null(stop.EntregadaAt);
            Assert.Equal(EstadoParadaEntrega.Pendiente, stop.Estado);
            Assert.All(await db.CodigosEntrega.Where(c => c.OrdenEntregaId == order.OrdenId).ToListAsync(), c => Assert.True(c.Reservado));
            Assert.Equal(app.PilotA, (await db.ParadasEntrega.SingleAsync(p => p.Id == order.Paradas[1].ParadaId)).PilotoAsignadoId);
        });
        var updated = await manager.GetStringAsync($"/Operaciones/Incidencias?id={incident}");
        Assert.Contains("Por reasignar", WebUtility.HtmlDecode(updated), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_handoff_HTTP_requires_csrf_and_replays_the_complete_versioned_selection()
    {
        var (order, incident) = await ScenarioAsync();
        List<VersionDevolucionRequest> returns = [];
        await app.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IOrdenService>().ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key());
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            foreach (var stop in order.Paradas)
            {
                var result = JsonSerializer.SerializeToElement(await service.SolicitarDevolucionAsync(
                    new(app.TenantA, "pilot-a", app.PilotA), new(order.OrdenId, stop.ParadaId, "Devolucion", "Retorno ficticio de QA", Key())));
                returns.Add(new(result.GetProperty("Id").GetInt32(), result.GetProperty("Version").GetString()!));
            }
        });
        using var manager = await app.LoginWebAsync("manager-a");
        var html = await manager.GetStringAsync($"/Operaciones/Reasignaciones?ordenId={order.OrdenId}&incidenciaId={incident}");
        Assert.Contains("Pasar devoluciones a reasignaci", WebUtility.HtmlDecode(html));
        var request = new TrasladarDevolucionesRequest(order.OrdenId, returns, "Traslado explícito de los paquetes", Key(), incident);
        using var forged = await manager.PostAsJsonAsync("/api/operaciones/devoluciones-a-reasignacion", request);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        await app.InScopeAsync(async sp => Assert.All(await sp.GetRequiredService<ApplicationDbContext>().DevolucionesOperativas.ToListAsync(), d => Assert.True(d.Abierta)));
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        manager.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value));
        using var result = await manager.PostAsJsonAsync("/api/operaciones/devoluciones-a-reasignacion", request);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = await result.Content.ReadAsStringAsync();
        using var replay = await manager.PostAsJsonAsync("/api/operaciones/devoluciones-a-reasignacion", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.RetirosParadas.CountAsync());
            Assert.All(await db.ParadasEntrega.ToListAsync(), p => { Assert.True(p.EnReasignacion); Assert.Null(p.PilotoAsignadoId); Assert.Equal(app.PilotA, p.PilotoCustodioId); Assert.Null(p.EntregadaAt); });
            Assert.All(await db.CodigosEntrega.Where(c => c.Tipo == TipoCodigo.C_Finalizacion).ToListAsync(), c => Assert.False(c.Usado));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pilot_HTTP_can_retire_classic_order_with_missing_or_empty_revision_and_replay(bool previousApp)
    {
        var (order, _) = await ScenarioAsync();
        using var pilot = await app.LoginAsync("pilot-a");
        pilot.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "4");
        var detail = await pilot.GetFromJsonAsync<JsonElement>($"/api/piloto/ordenes/{order.OrdenId}");
        var stops = detail.GetProperty("paradas").EnumerateArray().Select(p => new RevisionParadaRequest(
            p.GetProperty("paradaId").GetInt32(), previousApp ? null : p.GetProperty("revisionAsignacion").GetString())).ToList();
        var request = new SolicitarRetiroParadasRequest(order.OrdenId, stops,
            "Retirar todos los paquetes antes de recoger, sin B ni C", Key(), CrearIncidencia: true);
        using var response = await pilot.PostAsJsonAsync("/api/piloto/operaciones/retiros", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var replay = await pilot.PostAsJsonAsync("/api/piloto/operaciones/retiros", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        var ack = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal(2, ack.GetProperty("gestiones").GetArrayLength());
        var incidentId = ack.GetProperty("gestiones")[0].GetProperty("incidenciaId").GetInt32();
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var service = sp.GetRequiredService<EnvirapidoOperationsService>();
            var items = await db.RetirosParadas.OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, items.Count);
            Assert.All(items, x => { Assert.Equal("Solicitada", x.Estado); Assert.Equal(incidentId, x.IncidenciaId); });
            Assert.Equal(2, await db.IncidenciasOperativas.CountAsync()); // Existing report + one atomic report.
            foreach (var item in items)
                await service.DecidirRetiroParadaAsync(new(app.TenantA, "manager-a"), item.Id,
                    new("Aprobar", "Retiro autorizado sin finalizar la entrega", item.Version, Key()));
            Assert.All(await db.ParadasEntrega.ToListAsync(), p =>
            {
                Assert.Null(p.PilotoAsignadoId); Assert.Null(p.PilotoCustodioId); Assert.True(p.EnReasignacion);
                Assert.Null(p.EntregadaAt); Assert.Equal(EstadoParadaEntrega.Pendiente, p.Estado);
            });
            Assert.All(await db.CodigosEntrega.ToListAsync(), c => Assert.False(c.Usado));
        });
        using var oldDetail = await pilot.GetAsync($"/api/piloto/ordenes/{order.OrdenId}");
        Assert.Equal(HttpStatusCode.NoContent, oldDetail.StatusCode);
        // An absent revision never authorizes a command after conversion.
        using var stale = await pilot.PostAsJsonAsync("/api/piloto/operaciones/retiros", request with { OperacionId = Key() });
        Assert.False(stale.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Pilot_capability_advertises_atomic_link_only_to_authenticated_pilot()
    {
        using var pilot = await app.LoginAsync("pilot-a");
        var json = await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/capacidades");
        Assert.True(json.GetProperty("retiroVinculadoAIncidencia").GetBoolean());
        using var anonymous = app.CreateClient();
        using var denied = await anonymous.GetAsync("/api/piloto/capacidades");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var shop = await app.LoginAsync("shop-a");
        using var wrongRole = await shop.GetAsync("/api/piloto/capacidades");
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
    }
}
