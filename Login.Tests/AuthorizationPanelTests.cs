using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;
using Login.Models;
using Login.Data;
using Login.Domain.Entities;
using Login.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class AuthorizationPanelTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();

    public async Task InitializeAsync()
    {
        await app.SeedAsync();
        await app.InScopeAsync(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("SuperAdmin"))).Succeeded);
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            var admin = new IdentityUser { Id = "qa-admin", UserName = "qa-admin@example.test", Email = "qa-admin@example.test" };
            Assert.True((await users.CreateAsync(admin, PilotGoFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, "SuperAdmin")).Succeeded);
        });
    }

    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    [Theory]
    [InlineData("manager-a", "/Operaciones")]
    [InlineData("shop-a", "/Tienda/Ordenes")]
    [InlineData("pilot-a", "/Piloto/Index")]
    [InlineData("qa-admin", "/SuperAdmin/Dashboard")]
    public async Task Former_profile_links_render_a_safe_403_and_role_scoped_navigation(string user, string home)
    {
        using var client = await app.LoginWebAsync(user);
        foreach (var path in new[] { "/Identity/Account/Manage", "/Identity/Account/Manage/Index", "/Identity/Account/Manage/ChangePassword" })
        {
            var response = await client.GetAsync(path + "?returnUrl=https://untrusted.example/phish");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("No tienes autorización", WebUtility.HtmlDecode(html));
            Assert.Contains($"href=\"{home}\"", html);
            Assert.DoesNotContain("untrusted.example", html);
            Assert.DoesNotContain("ChangePassword", html);
            Assert.Contains("__RequestVerificationToken", html);
            Assert.Null(response.Headers.Location);
        }
    }

    [Theory]
    [InlineData("manager-a")]
    [InlineData("shop-a")]
    [InlineData("pilot-a")]
    public async Task Tenant_roles_cannot_open_or_mutate_the_global_administration(string user)
    {
        using var client = await app.LoginWebAsync(user);
        foreach (var url in new[] { "/SuperAdmin/Facturacion", "/SuperAdmin/CrearFactura", $"/SuperAdmin/DetalleCliente?id={app.TenantB}", "/SuperAdmin/Auditoria", "/SuperAdmin/Sistema" })
        {
            var protectedPage = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
            Assert.Contains("/Identity/Account/AccessDenied", protectedPage.Headers.Location!.ToString());
        }
        var denied = await client.GetAsync("/SuperAdmin/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/Identity/Account/AccessDenied", denied.Headers.Location!.ToString());
        var page = await client.GetAsync(denied.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("No tienes autorización", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Tenant B", html);
        Assert.DoesNotContain("Directorio de Empresas", html);

        var csrf = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(csrf);
        var mutation = await client.PostAsync("/SuperAdmin/TogglePlan", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["id"] = "1", ["__RequestVerificationToken"] = csrf }));
        Assert.Equal(HttpStatusCode.Redirect, mutation.StatusCode);
        Assert.Contains("/Identity/Account/AccessDenied", mutation.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Anonymous_denial_is_readable_and_the_profile_requires_sign_in()
    {
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var denial = await client.GetAsync("/Identity/Account/AccessDenied?ReturnUrl=//untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, denial.StatusCode);
        var html = await denial.Content.ReadAsStringAsync();
        Assert.Contains("No tienes autorización", WebUtility.HtmlDecode(html));
        Assert.Contains("href=\"/Identity/Account/Login\"", html);
        Assert.DoesNotContain("untrusted.example", html);
        Assert.DoesNotContain("Cerrar sesión", WebUtility.HtmlDecode(html));
        var profile = await client.GetAsync("/Identity/Account/Manage");
        Assert.Equal(HttpStatusCode.Redirect, profile.StatusCode);
        Assert.Contains("/Identity/Account/Login", profile.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Cookie_API_denials_are_JSON_and_never_redirect_to_HTML()
    {
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        await AssertJsonDenial(await anonymous.GetAsync("/api/operaciones/snapshot"), HttpStatusCode.Unauthorized);
        using var shop = await app.LoginWebAsync("shop-a");
        await AssertJsonDenial(await shop.GetAsync("/api/operaciones/snapshot"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Bearer_API_denials_are_JSON_and_do_not_accept_another_role()
    {
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        await AssertJsonDenial(await anonymous.GetAsync("/api/piloto/mis-ordenes"), HttpStatusCode.Unauthorized);
        using var shop = await app.LoginAsync("shop-a");
        await AssertJsonDenial(await shop.GetAsync("/api/piloto/mis-ordenes"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Denial_logout_requires_CSRF_and_keeps_the_user_signed_in_on_a_forged_post()
    {
        using var manager = await app.LoginWebAsync("manager-a");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/Identity/Account/Logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/Operaciones")).StatusCode);
        var response = await manager.GetAsync("/Identity/Account/AccessDenied");
        var html = await response.Content.ReadAsStringAsync();
        var csrf = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var logout = await manager.PostAsync("/Identity/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = csrf }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Contains("/Identity/Account/Login", logout.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await manager.GetAsync("/Operaciones")).StatusCode);
    }

    private static async Task AssertJsonDenial(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Generic_exception_destination_is_public_returns_500_and_never_exposes_details()
    {
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/Home/Error?error=secret-database-password&returnUrl=https://untrusted.example");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("No se pudo completar la solicitud", html);
        Assert.Contains("Referencia:", html);
        Assert.DoesNotContain("secret-database-password", html);
        Assert.DoesNotContain("untrusted.example", html);
        Assert.DoesNotContain("StackTrace", html);
        Assert.DoesNotContain("Development Mode", html);
    }

    [Fact]
    public void Exception_handler_keeps_API_response_JSON_without_leaking_the_exception()
    {
        var http = new DefaultHttpContext { TraceIdentifier = "qa-safe-trace" };
        http.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
            { Path = "/api/piloto/mis-ordenes", Error = new InvalidOperationException("Server=private;Password=secret") });
        var controller = new HomeController { ControllerContext = new ControllerContext { HttpContext = http } };
        var result = Assert.IsType<ObjectResult>(controller.Error());
        Assert.Equal(500, result.StatusCode);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("qa-safe-trace", json);
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("Server=", json);
        Assert.DoesNotContain("InvalidOperationException", json);
    }

    [Fact]
    public async Task Manager_plan_request_uses_server_tenant_and_never_upgrades_before_approval()
    {
        var nextPlanId = 0; var originalPlanId = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            originalPlanId = await db.ClientesApp.Where(t => t.Id == app.TenantA).Select(t => t.PlanId).SingleAsync();
            var plan = new Plan { Nombre = "Plan ampliado QA", PrecioMensual = 1000, MaxTiendas = 20, MaxPilotos = 20, MaxOrdenesPorMes = 2000 };
            db.Planes.Add(plan); await db.SaveChangesAsync(); nextPlanId = plan.Id;
            var billing = sp.GetRequiredService<SaasBillingService>();
            await billing.IssueInvoiceAsync(app.TenantB, billing.Today, billing.Today.AddMonths(1).AddDays(-1), billing.Today.AddDays(5), 700,
                "factura-privada-tenant-b", "qa-admin", Guid.NewGuid().ToString("N"));
        });
        using var manager = await app.LoginWebAsync("manager-a");
        var html = await manager.GetStringAsync($"/Suscripcion?empresaId={app.TenantB}");
        Assert.Contains("Tenant A", html);
        Assert.DoesNotContain("Tenant B", html);
        Assert.DoesNotContain("factura-privada-tenant-b", html);
        var input = new Dictionary<string, string>
        {
            ["planId"] = nextPlanId.ToString(), ["motivo"] = "Necesitamos ampliar capacidad", ["clave"] = Guid.NewGuid().ToString("N"),
            ["empresaId"] = app.TenantB.ToString(), ["tenantId"] = app.TenantB.ToString()
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/Suscripcion/SolicitarPlan", new FormUrlEncodedContent(input))).StatusCode);
        input["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(input["__RequestVerificationToken"]);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await manager.PostAsync("/Suscripcion/SolicitarPlan", new FormUrlEncodedContent(input));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.DoesNotContain("error=", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var request = Assert.Single(await db.SaasPlanRequests.ToListAsync());
            Assert.Equal(app.TenantA, request.ClienteAppId);
            Assert.Equal("manager-a", request.RequestedBy);
            Assert.Equal("Pendiente", request.Status);
            Assert.Equal(nextPlanId, request.RequestedPlanId);
            Assert.Equal(originalPlanId, await db.ClientesApp.Where(t => t.Id == app.TenantA).Select(t => t.PlanId).SingleAsync());
            Assert.Equal(originalPlanId, await db.ClientesApp.Where(t => t.Id == app.TenantB).Select(t => t.PlanId).SingleAsync());
        });
    }

    [Fact]
    public async Task Only_platform_admin_can_revoke_confirmed_mobile_sessions_and_platform_accounts_are_protected()
    {
        using var pilot = await app.LoginAsync("pilot-a");
        using var foreignPilot = await app.LoginAsync("pilot-b");
        using var manager = await app.LoginWebAsync("manager-a");
        using var admin = await app.LoginWebAsync("qa-admin");
        using var adminMobile = await app.LoginAsync("qa-admin");
        var deniedPage = await manager.GetAsync("/Identity/Account/AccessDenied");
        var deniedHtml = await deniedPage.Content.ReadAsStringAsync();
        var managerCsrf = WebUtility.HtmlDecode(Regex.Match(deniedHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var revoke = new Dictionary<string, string>
            { ["userId"] = "pilot-a", ["motivo"] = "Dispositivo extraviado QA", ["confirmado"] = "true", ["__RequestVerificationToken"] = managerCsrf };
        var forged = await manager.PostAsync("/SuperAdmin/RevocarSesiones", new FormUrlEncodedContent(revoke));
        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.Contains("AccessDenied", forged.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await pilot.GetAsync("/api/auth/me")).StatusCode);

        var adminHtml = await admin.GetStringAsync("/SuperAdmin/Accesos");
        var adminCsrf = WebUtility.HtmlDecode(Regex.Match(adminHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(adminCsrf);
        revoke.Remove("__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/SuperAdmin/RevocarSesiones", new FormUrlEncodedContent(revoke))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pilot.GetAsync("/api/auth/me")).StatusCode);
        revoke["__RequestVerificationToken"] = adminCsrf;
        revoke["confirmado"] = "false";
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/SuperAdmin/RevocarSesiones", new FormUrlEncodedContent(revoke))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pilot.GetAsync("/api/auth/me")).StatusCode);
        revoke["confirmado"] = "true";
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/SuperAdmin/RevocarSesiones", new FormUrlEncodedContent(revoke))).StatusCode);
        await AssertJsonDenial(await pilot.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, (await foreignPilot.GetAsync("/api/auth/me")).StatusCode);

        revoke["userId"] = "qa-admin";
        var protectedAccount = await admin.PostAsync("/SuperAdmin/RevocarSesiones", new FormUrlEncodedContent(revoke));
        Assert.Equal(HttpStatusCode.Redirect, protectedAccount.StatusCode);
        Assert.Contains("error=", protectedAccount.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await adminMobile.GetAsync("/api/auth/me")).StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.True(await db.Pilotos.Where(p => p.Id == app.PilotA).Select(p => p.Activo).SingleAsync());
            var record = Assert.Single(await db.SaasAuditLogs.Where(a => a.Action == "SesionesRevocadas").ToListAsync());
            Assert.Equal("pilot-a", record.EntityId);
            Assert.DoesNotContain("Token", record.Detail);
            Assert.DoesNotContain("Password", record.Detail);
        });
    }
}
