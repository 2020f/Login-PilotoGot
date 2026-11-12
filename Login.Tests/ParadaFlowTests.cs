using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ParadaFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task<OrdenCreadaResult> CrearAsync(HttpClient shop, string? key = null, int cantidad = 3)
    {
        var response = await shop.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA,
            operacionId = key ?? Guid.NewGuid().ToString("N"), destinatarios = Enumerable.Range(0, cantidad)
                .Select(i => new { usuarioFinalId = _app.CustomerA, instrucciones = $" Instrucción {i} " }).ToArray() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrdenCreadaResult>())!;
    }

    private async Task PrepararAsync(HttpClient pilot, OrdenCreadaResult orden, string? keyB = null)
    {
        pilot.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "3");
        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{orden.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { orden.OrdenId,
            orden.CodigoB, operacionId = keyB ?? Guid.NewGuid().ToString("N") })).EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> EntregarAsync(HttpClient pilot, OrdenCreadaResult orden,
        ParadaCreadaDto parada, string key, string? codigo = null)
        => pilot.PostAsJsonAsync($"/api/piloto/ordenes/{orden.OrdenId}/paradas/{parada.ParadaId}/confirmar-entrega",
            new { codigoC = codigo ?? parada.CodigoC, operacionId = key });

    [Fact]
    public async Task Concurrent_multi_creation_reserves_all_codes_once_and_keeps_one_order_and_quota()
    {
        using var shop = await _app.LoginAsync("shop-a");
        var key = Guid.NewGuid().ToString("N");
        var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CrearAsync(shop, key)));
        Assert.All(resultados, r => Assert.Equal(resultados[0], r));
        var orden = resultados[0];
        Assert.Null(orden.CodigoC);
        Assert.Equal(3, orden.Paradas.Count);
        Assert.Equal(3, orden.Paradas.Select(p => p.CodigoC).Distinct().Count());
        Assert.All(orden.Paradas, p => Assert.Matches("^[0-9]{4}$", p.CodigoC));
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.OrdenesEntrega.CountAsync());
            Assert.Equal(3, await db.ParadasEntrega.CountAsync());
            Assert.Equal(4, await db.CodigosEntrega.CountAsync());
            Assert.Equal(1, await db.OperacionesOrden.CountAsync());
            Assert.Null((await db.OrdenesEntrega.SingleAsync()).UsuarioFinalId);
            Assert.Null((await db.OrdenesEntrega.SingleAsync()).Precio);
            Assert.Equal(1, (await db.ClientesApp.FindAsync(_app.TenantA))!.OrdenSeq);
        });
    }

    [Fact]
    public async Task Each_C_closes_only_its_stop_and_replays_keep_original_result_after_parent_delivery()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var orden = await CrearAsync(shop);
        var bKey = Guid.NewGuid().ToString("N");
        await PrepararAsync(pilot, orden, bKey);
        var firstKey = Guid.NewGuid().ToString("N");
        var firstResponse = await EntregarAsync(pilot, orden, orden.Paradas[0], firstKey);
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<EntregaParadaResult>();
        Assert.Equal("Recolectada", first!.EstadoOrden);
        Assert.Equal(1, first.Entregadas);
        foreach (var p in orden.Paradas.Skip(1)) (await EntregarAsync(pilot, orden, p, Guid.NewGuid().ToString("N"))).EnsureSuccessStatusCode();
        var replay = await EntregarAsync(pilot, orden, orden.Paradas[0], firstKey);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(first, await replay.Content.ReadFromJsonAsync<EntregaParadaResult>());
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { orden.OrdenId, orden.CodigoB, operacionId = bKey })).EnsureSuccessStatusCode();
        var conflicto = await EntregarAsync(pilot, orden, orden.Paradas[1], firstKey);
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        Assert.False((await conflicto.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("solicitudRechazada", out _));
        var historial = await pilot.GetFromJsonAsync<PaginaOrdenesPilotoDto>("/api/piloto/historial?pagina=1&tamano=1");
        Assert.Equal(1, historial!.Total);
        Assert.Equal(3, Assert.Single(historial.Items).Entregadas);
        using var otro = await _app.LoginAsync("pilot-a2");
        Assert.Equal(0, (await otro.GetFromJsonAsync<PaginaOrdenesPilotoDto>("/api/piloto/historial"))!.Total);
        Assert.Equal("Entregada", (await pilot.GetFromJsonAsync<OrdenPilotoDto>($"/api/piloto/historial/{orden.OrdenId}"))!.Estado);
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Entregada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            Assert.All(await db.CodigosEntrega.ToListAsync(), c => Assert.False(c.Reservado));
            Assert.Equal(6, await db.HistorialOrdenes.CountAsync());
        });
    }

    [Fact]
    public async Task Wrong_stop_code_is_definitively_rejected_and_other_pilot_or_tenant_cannot_consume_it()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        using var another = await _app.LoginAsync("pilot-a2");
        using var foreign = await _app.LoginAsync("pilot-b");
        var orden = await CrearAsync(shop);
        await PrepararAsync(pilot, orden);
        var key = Guid.NewGuid().ToString("N");
        var incorrecto = await EntregarAsync(pilot, orden, orden.Paradas[0], key, orden.Paradas[1].CodigoC);
        Assert.Equal(HttpStatusCode.BadRequest, incorrecto.StatusCode);
        Assert.True((await incorrecto.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("solicitudRechazada").GetBoolean());
        foreach (var usuario in new[] { another, foreign }) {
            var response = await EntregarAsync(usuario, orden, orden.Paradas[0], Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("solicitudRechazada", out _));
        }
        (await EntregarAsync(pilot, orden, orden.Paradas[0], key)).EnsureSuccessStatusCode();
        await _app.InScopeAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<ApplicationDbContext>()
            .ParadasEntrega.CountAsync(p => p.Estado == EstadoParadaEntrega.Entregada)));
    }

    [Fact]
    public async Task Legacy_cannot_accept_multi_and_all_private_stop_fields_stay_hidden_until_B_then_use_snapshot()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var orden = await CrearAsync(shop);
        Assert.Empty((await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/ordenes-auto")).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await pilot.PostAsync($"/api/piloto/ordenes-auto/{orden.OrdenId}/aceptar", null)).StatusCode);
        pilot.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "3");
        var ofertas = await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/ordenes-auto");
        Assert.DoesNotContain("Destino A", ofertas.ToString());
        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{orden.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        var antesResponse = await pilot.GetAsync($"/api/piloto/ordenes/{orden.OrdenId}");
        Assert.True(antesResponse.IsSuccessStatusCode, await antesResponse.Content.ReadAsStringAsync());
        var antes = await antesResponse.Content.ReadFromJsonAsync<OrdenPilotoDto>();
        Assert.Equal("", antes!.UsuarioFinalNombre);
        Assert.All(antes.Paradas, p => { Assert.Equal("", p.Nombre); Assert.Null(p.Direccion); Assert.Null(p.Telefono);
            Assert.Null(p.MapaLink); Assert.Null(p.Latitud); Assert.Null(p.Longitud); Assert.Null(p.Instrucciones); });
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.UsuariosFinales.FindAsync(_app.CustomerA))!.DireccionUbicacion = "Destino modificado";
            await db.SaveChangesAsync();
        });
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { orden.OrdenId, orden.CodigoB,
            operacionId = Guid.NewGuid().ToString("N") })).EnsureSuccessStatusCode();
        var despues = await pilot.GetFromJsonAsync<OrdenPilotoDto>($"/api/piloto/ordenes/{orden.OrdenId}");
        Assert.All(despues!.Paradas, p => { Assert.Equal("Destino A", p.Nombre); Assert.Equal("Destino privado A", p.Direccion); });
        Assert.Equal(HttpStatusCode.BadRequest, (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega",
            new { orden.OrdenId, orden.CodigoB, codigoC = orden.Paradas[0].CodigoC })).StatusCode);
        var qr = await shop.GetFromJsonAsync<OrdenQrDto>($"/api/tienda/ordenes/{orden.OrdenId}/qr");
        Assert.Null(qr!.CodigoC); Assert.Equal(orden.Paradas, qr.Paradas);
    }

    [Fact]
    public async Task Simple_order_uses_C_only_and_wrong_B_can_be_corrected_without_consuming_operation()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var response = await shop.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA,
            operacionId = Guid.NewGuid().ToString("N") });
        response.EnsureSuccessStatusCode();
        var orden = (await response.Content.ReadFromJsonAsync<OrdenCreadaResult>())!;
        Assert.Equal(orden.CodigoC, Assert.Single(orden.Paradas).CodigoC);
        pilot.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "3");
        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{orden.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        var clave = Guid.NewGuid().ToString("N");
        var incorrecta = await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { orden.OrdenId,
            codigoB = orden.CodigoB == "0000" ? "0001" : "0000", operacionId = clave });
        Assert.Equal(HttpStatusCode.BadRequest, incorrecta.StatusCode);
        Assert.True((await incorrecta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("solicitudRechazada").GetBoolean());
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { orden.OrdenId, orden.CodigoB, operacionId = clave })).EnsureSuccessStatusCode();
        var entrega = await EntregarAsync(pilot, orden, orden.Paradas[0], Guid.NewGuid().ToString("N"));
        entrega.EnsureSuccessStatusCode();
        Assert.Equal("Entregada", (await entrega.Content.ReadFromJsonAsync<EntregaParadaResult>())!.EstadoOrden);
    }

    [Fact]
    public async Task Read_permissions_match_pickup_sequence_and_incident_pause_without_disclosing_destinations()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        pilot.DefaultRequestHeaders.Add("X-Contrato-Ordenes", "3");
        var first = await CrearAsync(shop, cantidad: 2); var second = await CrearAsync(shop, cantidad: 2);
        await _app.InScopeAsync(async sp => {
            var svc = sp.GetRequiredService<EnvirapidoOperationsService>();
            var actor = new OperationsActor(_app.TenantA, "manager-a");
            var ack = JsonSerializer.SerializeToElement(await svc.CrearRutaAsync(actor,
                new("Secuencia QA", DateOnly.FromDateTime(DateTime.UtcNow), [first.OrdenId, second.OrdenId], _app.PilotA, null, false, Guid.NewGuid().ToString("N"))));
            await svc.EstadoRutaAsync(actor, ack.GetProperty("id").GetInt32(),
                new("Iniciar", ack.GetProperty("version").GetString()!, Guid.NewGuid().ToString("N")));
        });
        var orders = (await pilot.GetFromJsonAsync<List<OrdenPilotoDto>>("/api/piloto/mis-ordenes"))!;
        Assert.True(orders.Single(o => o.OrdenId == first.OrdenId).PuedeRecoger);
        var later = orders.Single(o => o.OrdenId == second.OrdenId);
        Assert.False(later.PuedeRecoger); Assert.NotNull(later.BloqueoRecogida);
        Assert.All(orders.SelectMany(o => o.Paradas), p => { Assert.False(p.PuedeConfirmarEntrega); Assert.Equal("", p.Nombre); });
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { first.OrdenId, first.CodigoB, operacionId = Guid.NewGuid().ToString("N") })).EnsureSuccessStatusCode();
        var beforeSecondPickup = (await pilot.GetFromJsonAsync<OrdenPilotoDto>($"/api/piloto/ordenes/{first.OrdenId}"))!;
        Assert.False(beforeSecondPickup.Paradas[0].PuedeConfirmarEntrega);
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { second.OrdenId, second.CodigoB, operacionId = Guid.NewGuid().ToString("N") })).EnsureSuccessStatusCode();
        var ready = (await pilot.GetFromJsonAsync<OrdenPilotoDto>($"/api/piloto/ordenes/{first.OrdenId}"))!;
        Assert.True(ready.Paradas[0].PuedeConfirmarEntrega); Assert.False(ready.Paradas[1].PuedeConfirmarEntrega);
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.IncidenciasOperativas.Add(new() { ClienteAppId = _app.TenantA, OrdenEntregaId = first.OrdenId,
                ParadaEntregaId = first.Paradas[0].ParadaId, Categoria = "Sin contacto", Descripcion = "Pausa de lectura QA",
                Efecto = Login.Domain.Operations.EfectoIncidencia.PausarParada });
            await db.SaveChangesAsync();
        });
        var paused = (await pilot.GetFromJsonAsync<OrdenPilotoDto>($"/api/piloto/ordenes/{first.OrdenId}"))!;
        Assert.False(paused.Paradas[0].PuedeConfirmarEntrega); Assert.Contains("incidencia", paused.Paradas[0].BloqueoEntrega);
        var key = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Conflict, (await EntregarAsync(pilot, first, first.Paradas[0], key)).StatusCode);
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.IncidenciasOperativas.SingleAsync()).Estado = Login.Domain.Operations.EstadoIncidencia.Resuelta;
            await db.SaveChangesAsync();
        });
        var resumed = (await pilot.GetFromJsonAsync<OrdenPilotoDto>($"/api/piloto/ordenes/{first.OrdenId}"))!;
        Assert.True(resumed.Paradas[0].PuedeConfirmarEntrega);
        (await EntregarAsync(pilot, first, first.Paradas[0], key)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Database_rejects_C_linked_to_a_stop_from_another_order()
    {
        using var shop = await _app.LoginAsync("shop-a");
        var a = await CrearAsync(shop, cantidad: 1); var b = await CrearAsync(shop, cantidad: 1);
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var ajeno = await db.CodigosEntrega.SingleAsync(c => c.OrdenEntregaId == b.OrdenId && c.Tipo == TipoCodigo.C_Finalizacion);
            db.CodigosEntrega.Remove(ajeno); await db.SaveChangesAsync();
            var code = await db.CodigosEntrega.SingleAsync(c => c.OrdenEntregaId == a.OrdenId && c.Tipo == TipoCodigo.C_Finalizacion);
            code.ParadaEntregaId = b.Paradas[0].ParadaId;
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("FOREIGN KEY", error.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
        });
    }
}

public sealed class ParadaMigrationTests
{
    [SqlServerFact]
    public async Task Downgrade_refuses_to_discard_return_cases_and_preserves_data_and_migration_history()
    {
        using var app = new PilotGoFactory();
        const string latest = "20261112201846_EnvirapidoSaasBilling";
        const string protectedMigration = "20261112025055_EnvirapidoRetornosYCajaEmpresa";
        await app.SeedAsync(latest);
        await app.InScopeAsync(async sp => {
            var service = sp.GetRequiredService<IOrdenService>();
            var order = await service.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA,
                operacionId: Guid.NewGuid().ToString("N"));
            await service.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            await service.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Guid.NewGuid().ToString("N"));
            await sp.GetRequiredService<Login.Application.Operations.EnvirapidoOperationsService>()
                .SolicitarDevolucionAsync(new(app.TenantA, "pilot-a", app.PilotA),
                    new(order.OrdenId, order.Paradas[0].ParadaId, "Devolucion", "Entrega bloqueada", Guid.NewGuid().ToString("N")));
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>()
                .MigrateAsync("20261112021932_EnvirapidoParadasOperaciones"));
            Assert.Contains("Rollback bloqueado", error.GetBaseException().Message);
            // EF commits each migration independently. The empty SaaS schema may
            // be removed first; the protected returns migration must remain.
            Assert.Contains(protectedMigration, await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(1, await db.DevolucionesOperativas.CountAsync());
            Assert.Equal(EstadoOrden.Recolectada, (await db.OrdenesEntrega.AsNoTracking().SingleAsync()).Estado);
            Assert.False((await db.CodigosEntrega.SingleAsync(c => c.Tipo == TipoCodigo.C_Finalizacion)).Usado);
        });
    }

    [SqlServerFact]
    public async Task Legacy_incident_gets_a_paused_case_and_only_verified_stage_is_restored()
    {
        using var app = new PilotGoFactory();
        await app.SeedAsync("20260908024715_AddPilotoEnLinea");
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var instant = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
            foreach (var number in new[] { 88001, 88002 })
            {
                DateTime? pickup = number == 88001 ? instant : null;
                DateTime? delivered = number == 88002 ? instant : null;
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO OrdenesEntrega
                    (ClienteAppId,TiendaId,UsuarioFinalId,PilotoId,NumeroOrdenA,Estado,EsAuto,CreatedAt,AssignedAt,RecolectadaAt,EntregadaAt)
                    VALUES ({app.TenantA},{app.ShopA},{app.CustomerA},{app.PilotA},{number},5,0,{instant},{instant},{pickup},{delivered});");
                var id = await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM OrdenesEntrega WHERE NumeroOrdenA={number}").SingleAsync();
                foreach (var type in new[] { 1, 2 })
                {
                    var used = (type == 1 && pickup.HasValue) || (type == 2 && delivered.HasValue);
                    await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO CodigosEntrega
                        (OrdenEntregaId,Tipo,Codigo,Usado,CreatedAt) VALUES ({id},{type},{"INC"+number+type},{used},{instant});");
                }
            }
            await db.GetService<IMigrator>().MigrateAsync();
            var orders = await db.OrdenesEntrega.AsNoTracking().OrderBy(o => o.NumeroOrdenA).ToListAsync();
            Assert.Equal(EstadoOrden.Recolectada, orders[0].Estado);
            Assert.Equal(EstadoOrden.Asignada, orders[1].Estado);
            Assert.NotNull(orders[1].EntregadaAt); // Conservar el dato contradictorio para revisión, sin declarar entrega.
            var cases = await db.IncidenciasOperativas.AsNoTracking().OrderBy(i => i.OrdenEntregaId).ToListAsync();
            Assert.Equal(2, cases.Count);
            Assert.All(cases, i => { Assert.Equal("Legado", i.Categoria); Assert.Equal("System:migracion", i.ReportadaPor);
                Assert.Equal(Login.Domain.Operations.EfectoIncidencia.PausarRuta, i.Efecto); Assert.Null(i.VenceAt);
                Assert.StartsWith("MigracionIncidente:", i.ClaveDeteccion); });
            Assert.Contains("REVISIÓN MANUAL", cases[1].Descripcion);
            Assert.All(await db.HistorialOrdenes.ToListAsync(), h => Assert.Contains("Incidente (5)", h.Descripcion));
            await db.GetService<IMigrator>().MigrateAsync();
            Assert.Equal(2, await db.IncidenciasOperativas.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Migration_creates_one_stop_per_legacy_order_without_changing_existing_code_identity_or_snapshot()
    {
        using var app = new PilotGoFactory();
        await app.SeedAsync("20260908024715_AddPilotoEnLinea");
        await app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO OrdenesEntrega
                (ClienteAppId,TiendaId,UsuarioFinalId,NumeroOrdenA,Estado,EsAuto,CreatedAt)
                VALUES ({app.TenantA},{app.ShopA},{app.CustomerA},77777,4,0,'2026-11-10T12:00:00');");
            var id = await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM OrdenesEntrega WHERE NumeroOrdenA=77777").SingleAsync();
            foreach (var tipo in new[] {1,2}) await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO CodigosEntrega
                (OrdenEntregaId,Tipo,Codigo,Usado,CreatedAt) VALUES ({id},{tipo},{"LEGACY77777"+tipo},1,'2026-11-10T12:00:00');");
            var codeIds = await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM CodigosEntrega WHERE OrdenEntregaId={id} ORDER BY Id").ToListAsync();
            await db.GetService<IMigrator>().MigrateAsync();
            var orden = await db.OrdenesEntrega.Include(o => o.Paradas).Include(o => o.Codigos).SingleAsync();
            var parada = Assert.Single(orden.Paradas);
            Assert.Equal(id, orden.Id); Assert.Equal(77777, orden.NumeroOrdenA); Assert.Null(orden.CodigoA);
            Assert.Equal("Destino privado A", parada.Direccion);
            Assert.Equal(EstadoParadaEntrega.Entregada, parada.Estado);
            Assert.Equal(codeIds, orden.Codigos.OrderBy(c => c.Id).Select(c => c.Id));
            Assert.Equal(parada.Id, orden.Codigos.Single(c => c.Tipo == TipoCodigo.C_Finalizacion).ParadaEntregaId);
            Assert.All(orden.Codigos, c => { Assert.StartsWith("LEGACY77777", c.Codigo); Assert.True(c.Usado); Assert.False(c.Reservado); });
            await db.GetService<IMigrator>().MigrateAsync();
            Assert.Equal(1, await db.ParadasEntrega.CountAsync());
        });
    }
}
