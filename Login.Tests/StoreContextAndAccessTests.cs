using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Data;
using Login.Application.Interfaces;
using Login.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class StoreContextAndAccessTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Two_devices_keep_explicit_store_when_legacy_preference_changes()
    {
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.UsuariosTienda.Add(new UsuarioTienda { IdentityUserId = "multi", TiendaId = _app.ShopB, Activo = true });
            await db.SaveChangesAsync();
        });
        using var first = await _app.LoginAsync("multi");
        using var second = await _app.LoginAsync("multi");
        first.DefaultRequestHeaders.Add("X-Tienda-Id", _app.ShopA.ToString());
        second.DefaultRequestHeaders.Add("X-Tienda-Id", _app.ShopB.ToString());
        (await first.PostAsJsonAsync("/api/tienda/tienda-activa", new { tiendaId = _app.ShopA })).EnsureSuccessStatusCode();
        var changed = await second.PostAsJsonAsync("/api/tienda/tienda-activa", new { tiendaId = _app.ShopB });
        changed.EnsureSuccessStatusCode();
        Assert.Equal(_app.ShopB, (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
        Assert.Equal(_app.ShopA, (await first.GetFromJsonAsync<JsonElement>("/api/tienda/tienda-activa")).GetProperty("id").GetInt32());
        Assert.Equal(_app.ShopB, (await second.GetFromJsonAsync<JsonElement>("/api/tienda/tienda-activa")).GetProperty("id").GetInt32());
        var created = await first.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA, operacionId = Guid.NewGuid().ToString("N") });
        created.EnsureSuccessStatusCode();
        Assert.Single((await first.GetFromJsonAsync<JsonElement>("/api/tienda/ordenes")).EnumerateArray());
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/tienda/ordenes")).EnumerateArray());
        Assert.False((await second.PostAsJsonAsync("/api/tienda/ordenes", new { usuarioFinalId = _app.CustomerA, operacionId = Guid.NewGuid().ToString("N") })).IsSuccessStatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-store")]
    [InlineData("1,2")]
    public async Task Malformed_explicit_store_never_falls_back_to_a_valid_preference(string value)
    {
        using var client = await _app.LoginAsync("shop-a");
        client.DefaultRequestHeaders.Add("X-Tienda-Id", value);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tienda/tienda-activa")).StatusCode);
        Assert.False((await client.GetAsync("/api/tienda/ordenes")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Explicit_store_is_revalidated_after_membership_is_revoked()
    {
        using var client = await _app.LoginAsync("multi");
        client.DefaultRequestHeaders.Add("X-Tienda-Id", _app.ShopA2.ToString());
        (await client.GetAsync("/api/tienda/ordenes")).EnsureSuccessStatusCode();
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.UsuariosTienda.SingleAsync(u => u.IdentityUserId == "multi" && u.TiendaId == _app.ShopA2)).Activo = false;
            await db.SaveChangesAsync();
        });
        Assert.False((await client.GetAsync("/api/tienda/ordenes")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tienda/tienda-activa")).StatusCode);
    }

    [Fact]
    public async Task Creation_capabilities_are_authenticated_and_bound_to_an_authorized_store()
    {
        using var anonymous = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/tienda/capacidades")).StatusCode);
        using var shop = await _app.LoginAsync("shop-a");
        shop.DefaultRequestHeaders.Add("X-Tienda-Id", _app.ShopA.ToString());
        var response = await shop.GetAsync("/api/tienda/capacidades");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var contract = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, contract.GetProperty("versionContrato").GetInt32());
        Assert.True(contract.GetProperty("creacionIdempotente").GetBoolean());
        Assert.True(contract.GetProperty("contextoTienda").GetBoolean());
        shop.DefaultRequestHeaders.Remove("X-Tienda-Id");
        shop.DefaultRequestHeaders.Add("X-Tienda-Id", _app.ShopB.ToString());
        Assert.False((await shop.GetAsync("/api/tienda/capacidades")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task First_legacy_store_selection_is_safe_when_two_requests_race()
    {
        using var first = await _app.LoginAsync("multi");
        using var second = await _app.LoginAsync("multi");
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/tienda/tienda-activa", new { tiendaId = _app.ShopA }),
            second.PostAsJsonAsync("/api/tienda/tienda-activa", new { tiendaId = _app.ShopA2 }));
        Assert.All(responses, r => r.EnsureSuccessStatusCode());
        await _app.InScopeAsync(async sp => Assert.Equal(1,
            await sp.GetRequiredService<ApplicationDbContext>().UserContexts.CountAsync(c => c.IdentityUserId == "multi")));
    }

    [Fact]
    public async Task Store_selection_and_administrative_membership_change_have_consistent_lock_order()
    {
        var storeId = 0;
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var store = new Tienda { ClienteAppId = _app.TenantA, Nombre = "Comercio adicional QA", Direccion = "Calle QA" };
            db.Tiendas.Add(store); await db.SaveChangesAsync(); storeId = store.Id;
        });
        using var manager = await _app.LoginWebAsync("manager-a");
        var page = await manager.GetStringAsync("/Gestor/AsignarTiendaUsuario");
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Select()
        {
            await start.Task;
            for (var i = 0; i < 4; i++) await _app.InScopeAsync(sp => sp.GetRequiredService<IUserContextService>()
                .SetTiendaActivaIdAsync("multi", i % 2 == 0 ? _app.ShopA : _app.ShopA2));
        }
        async Task Assign()
        {
            await start.Task;
            var response = await manager.PostAsync("/Gestor/AsignarTiendaUsuario", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["EmailUsuario"] = "multi@example.test", ["TiendaId"] = storeId.ToString(),
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
            }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("ok=", response.Headers.Location?.OriginalString ?? "");
        }
        var selection = Select(); var assignment = Assign(); start.SetResult();
        await Task.WhenAll(selection, assignment);
        await _app.InScopeAsync(async sp => Assert.True(await sp.GetRequiredService<ApplicationDbContext>()
            .UsuariosTienda.AnyAsync(u => u.IdentityUserId == "multi" && u.TiendaId == storeId && u.Activo)));
    }

    [Fact]
    public async Task Failed_web_logins_lock_the_account_and_block_mobile_login_too()
    {
        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var page = await client.GetStringAsync("/Identity/Account/Login");
            var match = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(match.Success, "El login debe emitir protección CSRF para una prueba web real.");
            var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = "pilot-a@example.test", ["Input.Password"] = "Incorrect-password",
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
            }));
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);
        }
        await _app.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True(await users.IsLockedOutAsync((await users.FindByIdAsync("pilot-a"))!));
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new { email = "pilot-a@example.test", password = PilotGoFactory.Password })).StatusCode);
    }
}
