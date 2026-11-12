using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class OrderReliabilityTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static async Task<OrdenCreadaResult> CreateAsync(HttpClient client, int customer, string? key, string? note = "QA")
    {
        var response = await client.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = customer, operacionId = key, notaPedido = note });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrdenCreadaResult>())!;
    }

    [Fact]
    public async Task Concurrent_retries_create_one_order_with_original_codes_and_history()
    {
        using var shop = await _app.LoginAsync("shop-a");
        var key = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateAsync(shop, _app.CustomerA, key)));
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Matches("^[0-9]{4}$", results[0].CodigoA!);
        Assert.Matches("^[0-9]{4}$", results[0].CodigoB);
        Assert.Matches("^[0-9]{4}$", results[0].CodigoC);
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.OrdenesEntrega.CountAsync());
            Assert.Equal(1, await db.OperacionesOrden.CountAsync());
            Assert.Equal(1, await db.HistorialOrdenes.CountAsync());
            Assert.Equal(2, await db.CodigosEntrega.CountAsync());
            Assert.Equal(1, (await db.ClientesApp.FindAsync(_app.TenantA))!.OrdenSeq);
        });
    }

    [Fact]
    public async Task Replay_after_delivery_and_customer_deactivation_keeps_result_and_quota()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var key = Guid.NewGuid().ToString("N");
        var first = await CreateAsync(shop, _app.CustomerA, key.ToUpperInvariant(), "  QA  ");
        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{first.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { first.OrdenId, first.CodigoB })).EnsureSuccessStatusCode();
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega", new { first.OrdenId, first.CodigoB, first.CodigoC })).EnsureSuccessStatusCode();
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.UsuariosFinales.FindAsync(_app.CustomerA))!.Activo = false;
            (await db.ClientesApp.FindAsync(_app.TenantA))!.MaxOrdenesMes = 1;
            await db.SaveChangesAsync();
        });
        Assert.Equal(first, await CreateAsync(shop, _app.CustomerA, key, "QA"));
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.OrdenesEntrega.CountAsync());
            Assert.Equal(4, await db.HistorialOrdenes.CountAsync());
            Assert.Equal(EstadoOrden.Entregada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            Assert.Equal(1, (await db.ClientesApp.FindAsync(_app.TenantA))!.OrdenSeq);
        });
    }

    [Fact]
    public async Task Existing_key_with_different_payload_conflicts_without_discard_permission()
    {
        using var shop = await _app.LoginAsync("shop-a");
        var key = Guid.NewGuid().ToString("N");
        var original = await CreateAsync(shop, _app.CustomerA, key, "Original");
        var conflict = await shop.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA, notaPedido = "Otro pedido", operacionId = key });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.False((await conflict.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("solicitudRechazada", out _));
        Assert.Equal(original, await CreateAsync(shop, _app.CustomerA, key, "Original"));
    }

    [Fact]
    public async Task Operation_keys_are_scoped_to_actor_and_tenant_and_legacy_requests_still_create()
    {
        using var a = await _app.LoginAsync("shop-a");
        using var b = await _app.LoginAsync("shop-b");
        using var anotherA = await _app.LoginAsync("multi");
        var key = Guid.NewGuid().ToString("N");
        var first = await CreateAsync(a, _app.CustomerA, key);
        var second = await CreateAsync(b, _app.CustomerB, key);
        var third = await CreateAsync(anotherA, _app.CustomerA, key);
        Assert.Equal(3, new[] { first.OrdenId, second.OrdenId, third.OrdenId }.Distinct().Count());
        var legacy = await CreateAsync(a, _app.CustomerA, null);
        Assert.NotEqual(first.OrdenId, legacy.OrdenId);
        Assert.Matches("^[0-9]{4}$", legacy.CodigoB);
    }

    [Fact]
    public async Task Rejected_uncommitted_request_can_be_corrected_with_the_same_key()
    {
        using var shop = await _app.LoginAsync("shop-a");
        var key = Guid.NewGuid().ToString("N");
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.UsuariosFinales.FindAsync(_app.CustomerA))!.Activo = false;
            await db.SaveChangesAsync();
        });
        var rejected = await shop.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA, notaPedido = "QA", operacionId = key });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("solicitudRechazada").GetBoolean());
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.OrdenesEntrega.CountAsync());
            Assert.Equal(0, await db.OperacionesOrden.CountAsync());
            Assert.Equal(0, (await db.ClientesApp.FindAsync(_app.TenantA))!.OrdenSeq);
            (await db.UsuariosFinales.FindAsync(_app.CustomerA))!.Activo = true;
            await db.SaveChangesAsync();
        });
        await CreateAsync(shop, _app.CustomerA, key);
    }

    [Fact]
    public async Task Pickup_destination_contacts_and_null_coordinates_are_frozen_without_leaking_before_pickup()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var order = await CreateAsync(shop, _app.CustomerA, Guid.NewGuid().ToString("N"));
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var pickup = (await db.Tiendas.FindAsync(_app.ShopA))!;
            pickup.Nombre = "Comercio nuevo"; pickup.Direccion = "Recogida nueva"; pickup.Telefono = "9999999";
            pickup.Latitud = 18.5; pickup.Longitud = -69.9;
            var destination = (await db.UsuariosFinales.FindAsync(_app.CustomerA))!;
            destination.Nombre = "Otro destinatario"; destination.DireccionUbicacion = "Destino nuevo"; destination.Telefono = "8888888";
            destination.MapaLink = "https://maps.google.com/?q=18.6,-69.8";
            destination.Latitud = 18.6; destination.Longitud = -69.8;
            await db.SaveChangesAsync();
        });
        var receipt = await shop.GetFromJsonAsync<OrdenQrDto>($"/api/tienda/ordenes/{order.OrdenId}/qr");
        Assert.Equal("Destino A", receipt!.UsuarioFinalNombre);
        Assert.Equal("Destino privado A", receipt.DireccionUbicacion);
        Assert.Null(receipt.MapaLink);
        var offer = (await pilot.GetFromJsonAsync<List<OrdenAutoDisponibleDto>>("/api/piloto/ordenes-auto"))!.Single();
        Assert.Equal("A1", offer.TiendaNombre);
        Assert.Equal("Calle A", offer.TiendaDireccion);
        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{order.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        var assigned = await pilot.GetFromJsonAsync<OrdenPilotoDto>($"/api/piloto/ordenes/{order.OrdenId}");
        Assert.Equal("A1", assigned!.TiendaNombre);
        Assert.Null(assigned.TiendaLatitud);
        Assert.Null(assigned.TiendaLongitud);
        Assert.Equal("", assigned.UsuarioFinalDireccion);
        Assert.Equal("", assigned.UsuarioFinalTelefono);
        Assert.Null(assigned.UsuarioFinalMapaLink);
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { order.OrdenId, order.CodigoB })).EnsureSuccessStatusCode();
        var collected = await pilot.GetFromJsonAsync<OrdenPilotoDto>($"/api/piloto/ordenes/{order.OrdenId}");
        Assert.Equal("Destino privado A", collected!.UsuarioFinalDireccion);
        Assert.Equal("5550001", collected.UsuarioFinalTelefono);
        Assert.Null(collected.UsuarioFinalMapaLink);
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega", new { order.OrdenId, order.CodigoB, order.CodigoC })).EnsureSuccessStatusCode();
        Assert.Equal("Destino A", (await shop.GetFromJsonAsync<List<OrdenTiendaDto>>("/api/tienda/ordenes"))!.Single().UsuarioFinal);
        var next = await CreateAsync(shop, _app.CustomerA, Guid.NewGuid().ToString("N"));
        Assert.Equal("Destino nuevo", (await shop.GetFromJsonAsync<OrdenQrDto>($"/api/tienda/ordenes/{next.OrdenId}/qr"))!.DireccionUbicacion);
    }

    [Fact]
    public async Task Summary_counts_all_orders_and_uses_the_local_delivery_day()
    {
        var target = new DateTimeOffset(2026, 11, 12, 3, 30, 0, TimeSpan.Zero);
        _app.Clock.Advance(target - DateTimeOffset.UtcNow);
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var number = 0;
            void Add(EstadoOrden state, DateTime? delivered = null) => db.OrdenesEntrega.Add(new OrdenEntrega
            {
                ClienteAppId = _app.TenantA, TiendaId = _app.ShopA, UsuarioFinalId = _app.CustomerA,
                NumeroOrdenA = ++number, Estado = state, EntregadaAt = delivered,
                CreatedAt = state == EstadoOrden.Entregada ? target.UtcDateTime : target.UtcDateTime.AddDays(-10)
            });
            for (var i = 0; i < 201; i++) Add(EstadoOrden.Entregada, new DateTime(2026, 11, 11, 3, 59, 59, DateTimeKind.Utc));
            Add(EstadoOrden.Entregada, new DateTime(2026, 11, 12, 3, 29, 0, DateTimeKind.Utc));
            Add(EstadoOrden.Entregada, new DateTime(2026, 11, 12, 4, 0, 0, DateTimeKind.Utc));
            Add(EstadoOrden.Asignada); Add(EstadoOrden.Recolectada); Add(EstadoOrden.Creada); Add(EstadoOrden.Incidente);
            db.OrdenesEntrega.Add(new OrdenEntrega
            {
                ClienteAppId = _app.TenantA, TiendaId = _app.ShopA2, UsuarioFinalId = _app.CustomerA2,
                NumeroOrdenA = ++number, Estado = EstadoOrden.Asignada
            });
            db.OrdenesEntrega.Add(new OrdenEntrega
            {
                ClienteAppId = _app.TenantB, TiendaId = _app.ShopB, UsuarioFinalId = _app.CustomerB,
                NumeroOrdenA = 1, Estado = EstadoOrden.Asignada
            });
            await db.SaveChangesAsync();
        });
        using var shop = await _app.LoginAsync("shop-a");
        var listed = await shop.GetFromJsonAsync<List<OrdenTiendaDto>>("/api/tienda/ordenes");
        Assert.Equal(54, listed!.Count); // Todas las cuatro activas y cincuenta terminales recientes.
        Assert.Equal(50, listed.Count(o => o.Estado == "Entregada"));
        Assert.Equal(new[] { "Asignada", "Creada", "Incidente", "Recolectada" },
            listed.Where(o => o.Estado != "Entregada").Select(o => o.Estado).OrderBy(s => s));
        var summary = await shop.GetFromJsonAsync<ResumenOrdenesTiendaDto>("/api/tienda/ordenes/resumen");
        Assert.Equal(new ResumenOrdenesTiendaDto(1, 1, 1, 1, 1, 3), summary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pilot_deactivation_competing_with_assignment_never_strands_an_order(bool automatic)
    {
        using var shop = await _app.LoginAsync("shop-a");
        var order = await CreateAsync(shop, _app.CustomerA, Guid.NewGuid().ToString("N"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Assign()
        {
            await start.Task;
            try
            {
                await _app.InScopeAsync(async sp =>
                {
                    var service = sp.GetRequiredService<IOrdenService>();
                    if (automatic) await service.AceptarOrdenAutoAsync(order.OrdenId, _app.PilotA, "pilot-a", "Piloto");
                    else await service.AsignarPilotoAsync(order.CodigoB, _app.PilotA, "manager-a", "Gestor", order.OrdenId);
                });
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        async Task<bool> Deactivate()
        {
            await start.Task;
            try
            {
                await _app.InScopeAsync(async sp =>
                {
                    var db = sp.GetRequiredService<ApplicationDbContext>();
                    var quotas = sp.GetRequiredService<QuotaPolicyService>();
                    using var tx = await quotas.BeginTenantMutationAsync(_app.TenantA);
                    await quotas.EnsureNoOpenPilotOrdersAsync(_app.TenantA, _app.PilotA);
                    var pilot = (await db.Pilotos.FindAsync(_app.PilotA))!;
                    pilot.Activo = false;
                    pilot.Estado = EstadoPiloto.Inactivo;
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        var assign = Assign(); var deactivate = Deactivate(); start.SetResult();
        var outcomes = await Task.WhenAll(assign, deactivate);
        Assert.Single(outcomes, succeeded => succeeded);
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var pilot = (await db.Pilotos.FindAsync(_app.PilotA))!;
            var current = (await db.OrdenesEntrega.FindAsync(order.OrdenId))!;
            Assert.False(!pilot.Activo && current.PilotoId == pilot.Id);
            Assert.Equal(pilot.Activo ? EstadoOrden.Asignada : EstadoOrden.Creada, current.Estado);
        });
    }

    [Fact]
    public async Task Web_creation_redirect_contains_identifiers_and_replaying_the_form_does_not_duplicate()
    {
        using var web = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginHtml = await web.GetStringAsync("/Identity/Account/Login");
        var login = await web.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "shop-a@example.test", ["Input.Password"] = PilotGoFactory.Password,
            ["__RequestVerificationToken"] = Hidden(loginHtml, "__RequestVerificationToken")
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var html = await web.GetStringAsync("/Tienda/CrearOrden");
        var body = new Dictionary<string, string>
        {
            ["TiendaId"] = _app.ShopA.ToString(), ["UsuarioFinalId"] = _app.CustomerA.ToString(), ["NotaPedido"] = "QA",
            ["OperacionId"] = Hidden(html, "OperacionId"), ["__RequestVerificationToken"] = Hidden(html, "__RequestVerificationToken")
        };
        var first = await web.PostAsync("/Tienda/CrearOrdenPost", new FormUrlEncodedContent(body));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var location = first.Headers.Location!.OriginalString;
        Assert.Contains("Comprobante?ordenId=", location);
        Assert.Contains("tiendaId=", location);
        Assert.DoesNotContain("codigo", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ok=", location, StringComparison.OrdinalIgnoreCase);
        var replay = await web.PostAsync("/Tienda/CrearOrdenPost", new FormUrlEncodedContent(body));
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        var receipt = await web.GetAsync(location);
        receipt.EnsureSuccessStatusCode();
        Assert.True(receipt.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("?codigo=", await receipt.Content.ReadAsStringAsync());
        await _app.InScopeAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<ApplicationDbContext>().OrdenesEntrega.CountAsync()));
    }

    private static string Hidden(string html, string name)
    {
        foreach (Match match in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
            if (Regex.IsMatch(match.Value, $"\\bname=\"{Regex.Escape(name)}\""))
                return WebUtility.HtmlDecode(Regex.Match(match.Value, "\\bvalue=\"([^\"]*)\"").Groups[1].Value);
        throw new InvalidOperationException($"No se encontró el campo {name}.");
    }

    [Fact]
    public async Task Availability_is_derived_from_workload_even_when_stored_status_is_stale()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var order = await CreateAsync(shop, _app.CustomerA, Guid.NewGuid().ToString("N"));
        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{order.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        var assigned = await pilot.GetFromJsonAsync<PilotoEstadoDto>("/api/piloto/estado");
        Assert.True(assigned!.EnLinea);
        Assert.Equal("Ocupado", assigned.Estado);
        (await pilot.PostAsJsonAsync("/api/piloto/estado", new { enLinea = false })).EnsureSuccessStatusCode();
        var offlineBusy = await pilot.GetFromJsonAsync<PilotoEstadoDto>("/api/piloto/estado");
        Assert.False(offlineBusy!.EnLinea);
        Assert.Equal("Ocupado", offlineBusy.Estado);
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { order.OrdenId, order.CodigoB })).EnsureSuccessStatusCode();
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega", new { order.OrdenId, order.CodigoB, order.CodigoC })).EnsureSuccessStatusCode();
        var completed = await pilot.GetFromJsonAsync<PilotoEstadoDto>("/api/piloto/estado");
        Assert.False(completed!.EnLinea);
        Assert.Equal("Disponible", completed.Estado);
    }

    [Fact]
    public async Task Pilot_cannot_be_reconnected_by_a_request_competing_with_deactivation()
    {
        for (var round = 0; round < 6; round++)
        {
            await _app.InScopeAsync(async sp =>
            {
                var db = sp.GetRequiredService<ApplicationDbContext>();
                var p = (await db.Pilotos.FindAsync(_app.PilotA))!;
                p.Activo = true; p.EnLinea = false; p.Estado = EstadoPiloto.Disponible;
                await db.SaveChangesAsync();
            });
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Connect()
            {
                await start.Task;
                try
                {
                    await _app.InScopeAsync(sp => sp.GetRequiredService<IPilotoAppService>().SetEnLineaAsync("pilot-a", true));
                }
                catch (InvalidOperationException) { /* La baja ganó el bloqueo. */ }
            }
            async Task Deactivate()
            {
                await start.Task;
                await _app.InScopeAsync(async sp =>
                {
                    var db = sp.GetRequiredService<ApplicationDbContext>();
                    var quotas = sp.GetRequiredService<QuotaPolicyService>();
                    using var tx = await quotas.BeginTenantMutationAsync(_app.TenantA);
                    await quotas.EnsureNoOpenPilotOrdersAsync(_app.TenantA, _app.PilotA);
                    var p = (await db.Pilotos.FindAsync(_app.PilotA))!;
                    p.Activo = false; p.EnLinea = false; p.Estado = EstadoPiloto.Inactivo;
                    await db.SaveChangesAsync(); await tx.CommitAsync();
                });
            }
            var connect = Connect(); var deactivate = Deactivate(); start.SetResult();
            await Task.WhenAll(connect, deactivate);
            await _app.InScopeAsync(async sp =>
            {
                var p = (await sp.GetRequiredService<ApplicationDbContext>().Pilotos.FindAsync(_app.PilotA))!;
                Assert.False(p.Activo); Assert.False(p.EnLinea); Assert.Equal(EstadoPiloto.Inactivo, p.Estado);
                await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<IPilotoAppService>().SetEnLineaAsync("pilot-a", true));
            });
        }
    }

    [Fact]
    public async Task Negative_order_quota_is_rejected_and_does_not_act_as_unlimited()
    {
        using var shop = await _app.LoginAsync("shop-a");
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.ClientesApp.FindAsync(_app.TenantA))!.MaxOrdenesMes = -1;
            await db.SaveChangesAsync();
        });
        var rejected = await shop.PostAsJsonAsync("/api/tienda/ordenes", new
        {
            usuarioFinalId = _app.CustomerA, operacionId = Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("solicitudRechazada").GetBoolean());
        await _app.InScopeAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<ApplicationDbContext>().OrdenesEntrega.CountAsync()));
    }

    [Fact]
    public async Task Guid_X_format_cannot_bypass_the_operation_key_length_limit()
    {
        using var shop = await _app.LoginAsync("shop-a");
        var key = Guid.NewGuid().ToString("X");
        Assert.True(key.Length > 64);
        var rejected = await shop.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA, operacionId = key });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await _app.InScopeAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<ApplicationDbContext>().OperacionesOrden.CountAsync()));
    }
}
