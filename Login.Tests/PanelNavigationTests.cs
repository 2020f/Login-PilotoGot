using System.Net;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class PanelNavigationTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Superadmin_pages_and_their_local_scripts_are_connected()
    {
        await _app.InScopeAsync(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("SuperAdmin"))).Succeeded);
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            var admin = new IdentityUser { Id = "qa-admin", UserName = "qa-admin@example.test", Email = "qa-admin@example.test" };
            Assert.True((await users.CreateAsync(admin, PilotGoFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, "SuperAdmin")).Succeeded);
        });
        using var adminClient = await _app.LoginWebAsync("qa-admin");
        foreach (var url in new[] { "/SuperAdmin/Dashboard", "/SuperAdmin/Clientes", "/SuperAdmin/Planes",
            "/SuperAdmin/CrearCliente", $"/SuperAdmin/EditarCliente?id={_app.TenantA}", "/SuperAdmin/CrearPlan" })
        {
            var response = await adminClient.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("/SuperAdmin/Finanzas", html);
            Assert.Contains("/SuperAdmin/Auditoria", html);
            Assert.DoesNotContain("Carlos Admin", html);
            foreach (Match script in Regex.Matches(html, "<script[^>]*src=\"(/[^\"]+\\.js(?:\\?[^\"]*)?)\""))
                Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync(WebUtility.HtmlDecode(script.Groups[1].Value))).StatusCode);
        }
    }

    [Fact]
    public async Task Manager_pages_render_without_nested_forms_and_tenant_B_is_not_visible()
    {
        using var client = await _app.LoginWebAsync("manager-a");
        foreach (var url in new[] { "/Supervisor/Ordenes", "/Supervisor/Ordenes?historial=true",
            "/Supervisor/OrdenesFragmento", "/Supervisor/Pilotos", "/Supervisor/Tiendas",
            $"/Supervisor/EditarPiloto?id={_app.PilotA}", $"/Supervisor/EditarTienda?id={_app.ShopA}" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Piloto B", html);
            var depth = 0;
            foreach (Match form in Regex.Matches(html, "</?form\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                if (form.Value.StartsWith("</")) depth--;
                else { Assert.Equal(0, depth); depth++; }
                Assert.InRange(depth, 0, 1);
            }
            Assert.Equal(0, depth);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Supervisor/DetalleOrden?id=999999")).StatusCode);
    }

    [Fact]
    public async Task QR_is_requested_by_order_and_rechecks_web_membership_without_putting_PIN_in_URL()
    {
        var orderId = 0;
        await _app.InScopeAsync(async sp => orderId = (await sp.GetRequiredService<IOrdenService>()
            .CrearOrdenAsync(_app.TenantA, _app.ShopA, _app.CustomerA)).OrdenId);
        using var owner = await _app.LoginWebAsync("shop-a");
        var receipt = await owner.GetStringAsync($"/Tienda/Comprobante?ordenId={orderId}&tiendaId={_app.ShopA}");
        Assert.Contains("/Qr/Orden?ordenId=", receipt);
        Assert.DoesNotContain("/Qr/Generar?codigo=", receipt);
        var png = await owner.GetAsync($"/Qr/Orden?ordenId={orderId}&tiendaId={_app.ShopA}");
        Assert.Equal(HttpStatusCode.OK, png.StatusCode);
        Assert.Equal("image/png", png.Content.Headers.ContentType?.MediaType);
        Assert.True(png.Headers.CacheControl?.NoStore);
        using var other = await _app.LoginWebAsync("shop-b");
        Assert.False((await other.GetAsync($"/Qr/Orden?ordenId={orderId}")).IsSuccessStatusCode);
        Assert.False((await other.GetAsync($"/Tienda/Comprobante?ordenId={orderId}&tiendaId={_app.ShopA}")).IsSuccessStatusCode);
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.UsuariosTienda.SingleAsync(x => x.IdentityUserId == "shop-a" && x.TiendaId == _app.ShopA)).Activo = false;
            await db.SaveChangesAsync();
        });
        Assert.False((await owner.GetAsync($"/Qr/Orden?ordenId={orderId}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Administrative_mutations_require_CSRF_and_reject_store_users()
    {
        using var manager = await _app.LoginWebAsync("manager-a");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/Supervisor/DesactivarPiloto?id={_app.PilotA}", null)).StatusCode);
        using var shop = await _app.LoginWebAsync("shop-a");
        Assert.Equal(HttpStatusCode.Redirect, (await shop.GetAsync("/Supervisor/Ordenes")).StatusCode);
        await _app.InScopeAsync(async sp => Assert.True((await sp.GetRequiredService<ApplicationDbContext>().Pilotos.FindAsync(_app.PilotA))!.Activo));
    }
}
