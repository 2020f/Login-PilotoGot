using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class FleetFlowTests
{
    [Fact]
    public async Task Twenty_pilots_and_forty_shops_complete_two_rounds_with_original_sessions()
    {
        using var app = new PilotGoFactory();
        await app.SeedAsync();
        var pilots = new List<Piloto>();
        var customers = new List<UsuarioFinal>();
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            async Task AddUser(string id, string role)
            {
                var user = new IdentityUser { Id = id, UserName = $"{id}@example.test", Email = $"{id}@example.test" };
                Assert.True((await users.CreateAsync(user, PilotGoFactory.Password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            }
            var plan = await db.Planes.SingleAsync();
            plan.MaxTiendas = 100;
            plan.MaxPilotos = 100;
            await AddUser("manager-b", "Gestor");
            (await db.ClientesApp.FindAsync(app.TenantB))!.GestorIdentityUserId = "manager-b";
            for (var i = 0; i < 40; i++)
            {
                var tenant = i % 2 == 0 ? app.TenantA : app.TenantB;
                var userId = $"fleet-shop-{i}";
                await AddUser(userId, "Cliente");
                var shop = new Tienda { ClienteAppId = tenant, Nombre = $"Comercio {i}", Direccion = $"Calle {i}", AutoAsignacion = i % 20 % 3 != 1 };
                var customer = new UsuarioFinal { Tienda = shop, Nombre = $"Destino {i}", DireccionUbicacion = $"Destino {i}" };
                customers.Add(customer);
                db.Add(customer);
                db.Add(new UsuarioTienda { Tienda = shop, IdentityUserId = userId, Activo = true });
                if (i < 20)
                {
                    var pilotUser = $"fleet-pilot-{i}";
                    await AddUser(pilotUser, "Piloto");
                    var pilot = new Piloto { ClienteAppId = tenant, IdentityUserId = pilotUser, Nombre = $"Piloto {i}", EnLinea = true };
                    pilots.Add(pilot);
                    db.Add(pilot);
                }
            }
            await db.SaveChangesAsync();
        });
        var shopClients = new List<HttpClient>();
        var pilotClients = new List<HttpClient>();
        try
        {
            for (var i = 0; i < 40; i++) shopClients.Add(await app.LoginAsync($"fleet-shop-{i}"));
            for (var i = 0; i < 20; i++) pilotClients.Add(await app.LoginAsync($"fleet-pilot-{i}"));
            for (var round = 0; round < 2; round++)
            {
                // Cinco solicitudes concurrentes; veinte pilotos conservan su sesión
                // durante ambas rondas y cuarenta comercios crean una orden cada uno.
                foreach (var batch in Enumerable.Range(0, 20).Chunk(5))
                    await Task.WhenAll(batch.Select(async i =>
                    {
                        var shopIndex = round * 20 + i;
                        var response = await shopClients[shopIndex].PostAsJsonAsync("/api/tienda/ordenes",
                            new { usuarioFinalId = customers[shopIndex].Id });
                        response.EnsureSuccessStatusCode();
                        var order = await response.Content.ReadFromJsonAsync<OrdenCreadaResult>();
                        Assert.NotNull(order);
                        var pilot = pilotClients[i];
                        if (customers[shopIndex].Tienda.AutoAsignacion)
                        {
                            var offers = await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/ordenes-auto");
                            Assert.Contains(offers.EnumerateArray(), o => o.GetProperty("ordenId").GetInt32() == order.OrdenId);
                            (await pilot.PostAsync($"/api/piloto/ordenes-auto/{order.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
                        }
                        else
                        {
                            (await pilot.PostAsJsonAsync("/api/piloto/estado", new { enLinea = false })).EnsureSuccessStatusCode();
                            await app.InScopeAsync(sp => sp.GetRequiredService<IOrdenService>().AsignarPilotoAsync(
                                order.CodigoB, pilots[i].Id, i % 2 == 0 ? "manager-a" : "manager-b", "Gestor", order.OrdenId));
                        }
                        var assigned = await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/mis-ordenes");
                        Assert.Single(assigned.EnumerateArray());
                        Assert.Equal(order.OrdenId, assigned[0].GetProperty("ordenId").GetInt32());
                        Assert.Equal(System.Net.HttpStatusCode.NoContent,
                            (await pilotClients[(i + 1) % 20].GetAsync($"/api/piloto/ordenes/{order.OrdenId}")).StatusCode);
                        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { order.OrdenId, order.CodigoB })).EnsureSuccessStatusCode();
                        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega", new { order.OrdenId, order.CodigoB, order.CodigoC })).EnsureSuccessStatusCode();
                        Assert.Empty((await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/mis-ordenes")).EnumerateArray());
                    }));
            }
            await app.InScopeAsync(async sp =>
            {
                var db = sp.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(40, await db.OrdenesEntrega.CountAsync(o => o.Estado == EstadoOrden.Entregada));
                Assert.Equal(160, await db.HistorialOrdenes.CountAsync());
                Assert.Equal(0, await db.CodigosEntrega.CountAsync(c => c.Reservado));
            });
        }
        finally
        {
            foreach (var client in shopClients.Concat(pilotClients)) client.Dispose();
        }
    }
}
