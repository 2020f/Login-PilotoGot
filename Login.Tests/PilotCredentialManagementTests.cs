using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class PilotCredentialManagementTests : IAsyncLifetime
{
    private const string Replacement = "Pilot-Replacement42!";
    private const string UpdatedEmail = "updated-pilot@example.test";
    private readonly PilotGoFactory app = new();
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    private static string Csrf(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private Dictionary<string, string> Form(string email = "pilot-a@example.test", string? password = null, string? confirmation = null)
        => new()
        {
            ["Id"] = app.PilotA.ToString(), ["Nombre"] = "Piloto actualizado", ["Telefono"] = "8090001234",
            ["Activo"] = "true", ["EmailPiloto"] = email,
            ["NuevoPassword"] = password ?? "", ["ConfirmarPassword"] = confirmation ?? password ?? ""
        };

    private static async Task<HttpResponseMessage> Edit(HttpClient client, Dictionary<string, string> form, bool csrf = true)
    {
        if (csrf) form["__RequestVerificationToken"] = Csrf(await client.GetStringAsync("/Identity/Account/Login"));
        return await client.PostAsync("/Supervisor/EditarPiloto", new FormUrlEncodedContent(form));
    }

    private sealed record Session(HttpClient Client, MobileLoginResponse Data) : IDisposable
    { public void Dispose() => Client.Dispose(); }

    private async Task<Session> Login(string email = "pilot-a@example.test", string password = PilotGoFactory.Password)
    {
        var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<MobileLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.Token);
        return new(client, data);
    }

    private async Task<HttpStatusCode> LoginStatus(string email, string password)
    {
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        return response.StatusCode;
    }

    private async Task<string> IdentityState(string id = "pilot-a")
    {
        string state = "";
        await app.InScopeAsync(async services =>
        {
            var u = await services.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
            state = JsonSerializer.Serialize(new { u.Email, u.UserName, u.NormalizedEmail, u.NormalizedUserName,
                u.PasswordHash, u.SecurityStamp, u.EmailConfirmed, u.LockoutEnd });
        });
        return state;
    }

    private async Task<string> PilotState(int? id = null)
    {
        string state = "";
        await app.InScopeAsync(async services =>
        {
            var p = await services.GetRequiredService<ApplicationDbContext>().Pilotos.AsNoTracking().SingleAsync(p => p.Id == (id ?? app.PilotA));
            state = JsonSerializer.Serialize(new { p.Nombre, p.Telefono, p.Activo, p.EnLinea, p.Estado, p.ClienteAppId, p.IdentityUserId });
        });
        return state;
    }

    private async Task<string> WorkState()
    {
        string state = "";
        await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            state = JsonSerializer.Serialize(new
            {
                Orders = await db.OrdenesEntrega.AsNoTracking().OrderBy(o => o.Id).Select(o => new
                    { o.Id, o.ClienteAppId, o.PilotoId, o.Estado, o.CodigoA, o.AssignedAt, o.RecolectadaAt, o.EntregadaAt }).ToListAsync(),
                Codes = await db.CodigosEntrega.AsNoTracking().OrderBy(c => c.Id).Select(c => new
                    { c.Id, c.OrdenEntregaId, c.ClienteAppId, c.Tipo, c.Codigo, c.Reservado, c.Usado }).ToListAsync(),
                Routes = await db.RutasOperativas.AsNoTracking().OrderBy(r => r.Id).Select(r => new
                    { r.Id, r.ClienteAppId, r.PilotoId, r.Estado, r.Version, r.Fecha }).ToListAsync(),
                Points = await db.PuntosRutaOperativos.AsNoTracking().OrderBy(p => p.Id).Select(p => new
                    { p.Id, p.RutaId, p.OrdenEntregaId, p.Tipo, p.Posicion, p.LlegadaAt }).ToListAsync(),
                Memberships = await db.RutaOrdenesOperativas.AsNoTracking().OrderBy(m => m.Id).Select(m => new
                    { m.Id, m.RutaId, m.OrdenEntregaId, m.ClienteAppId, m.Vigente }).ToListAsync()
            });
        });
        return state;
    }

    private async Task SeedActiveWork(bool route = false)
        => await app.InScopeAsync(async services =>
        {
            var orders = services.GetRequiredService<IOrdenService>();
            var order = await orders.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA);
            if (!route)
            {
                await orders.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
                return;
            }
            var ops = services.GetRequiredService<EnvirapidoOperationsService>();
            var manager = await ops.ManagerAsync("manager-a");
            await ops.CrearRutaAsync(manager, new("Ruta que conserva sus credenciales", DateOnly.FromDateTime(app.Clock.GetUtcNow().AddHours(-4).DateTime),
                [order.OrdenId], app.PilotA, null, false, Guid.NewGuid().ToString("N")));
            var planned = Assert.Single((await ops.SnapshotAsync(await ops.PilotAsync("pilot-a"))).Rutas);
            await ops.EstadoRutaAsync(manager, planned.Id, new("Iniciar", planned.Version, Guid.NewGuid().ToString("N")));
        });

    [Fact]
    public async Task Edit_form_exposes_email_and_blank_password_fields_without_disclosing_credentials()
    {
        using var manager = await app.LoginWebAsync("manager-a");
        var html = await manager.GetStringAsync($"/Supervisor/EditarPiloto?id={app.PilotA}");
        var email = Regex.Match(html, "<input[^>]*name=\"EmailPiloto\"[^>]*>").Value;
        Assert.NotEmpty(email);
        Assert.DoesNotContain("readonly", email, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disabled", email, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pilot-a@example.test", html);
        foreach (var name in new[] { "NuevoPassword", "ConfirmarPassword" })
        {
            var field = Regex.Match(html, $"<input[^>]*name=\"{name}\"[^>]*>").Value;
            Assert.Contains("type=\"password\"", field);
            Assert.DoesNotMatch("value=\"[^\"]+\"", field);
        }
        Assert.DoesNotContain(PilotGoFactory.Password, html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Credentials_change_updates_login_revokes_only_target_sessions_and_preserves_work(bool emailChange, bool passwordChange)
    {
        await SeedActiveWork();
        var work = await WorkState();
        var foreignIdentity = await IdentityState("pilot-b");
        using var old = await Login();
        using var unrelated = await Login("pilot-b@example.test");
        using var manager = await app.LoginWebAsync("manager-a");
        var form = Form(emailChange ? "  UPDATED-PILOT@EXAMPLE.TEST  " : "pilot-a@example.test", passwordChange ? Replacement : null);
        form["IdentityUserId"] = "pilot-b"; form["ClienteAppId"] = app.TenantB.ToString();
        using var response = await Edit(manager, form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Supervisor/Pilotos", response.Headers.Location!.OriginalString);
        var nextEmail = emailChange ? UpdatedEmail : "pilot-a@example.test";
        await app.InScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByIdAsync("pilot-a"))!;
            Assert.Equal(nextEmail, user.Email); Assert.Equal(nextEmail, user.UserName);
            Assert.Equal(nextEmail.ToUpperInvariant(), user.NormalizedEmail);
            Assert.Equal(nextEmail.ToUpperInvariant(), user.NormalizedUserName);
            Assert.True(await users.CheckPasswordAsync(user, passwordChange ? Replacement : PilotGoFactory.Password));
            var p = await services.GetRequiredService<ApplicationDbContext>().Pilotos.FindAsync(app.PilotA);
            Assert.Equal("pilot-a", p!.IdentityUserId); Assert.Equal(app.TenantA, p.ClienteAppId);
            Assert.True(p.Activo); Assert.True(p.EnLinea); Assert.Equal(EstadoPiloto.Ocupado, p.Estado);
        });
        using var next = await Login(nextEmail, passwordChange ? Replacement : PilotGoFactory.Password);
        Assert.Equal(HttpStatusCode.OK, (await next.Client.GetAsync("/api/piloto/mis-ordenes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatus("pilot-a@example.test", PilotGoFactory.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.Client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.Client.PostAsJsonAsync("/api/auth/refresh", new { old.Data.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await unrelated.Client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(foreignIdentity, await IdentityState("pilot-b"));
        Assert.Equal(work, await WorkState());
    }

    [Fact]
    public async Task Profile_only_edit_preserves_password_stamp_session_and_active_route()
    {
        await SeedActiveWork(route: true);
        var work = await WorkState(); var identity = await IdentityState();
        using var pilot = await Login();
        using var manager = await app.LoginWebAsync("manager-a");
        using var response = await Edit(manager, Form());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(identity, await IdentityState()); Assert.Equal(work, await WorkState());
        Assert.Equal(HttpStatusCode.OK, (await pilot.Client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pilot.Client.PostAsJsonAsync("/api/auth/refresh", new { pilot.Data.RefreshToken })).StatusCode);
        await app.InScopeAsync(async services =>
        {
            var p = await services.GetRequiredService<ApplicationDbContext>().Pilotos.FindAsync(app.PilotA);
            Assert.Equal("Piloto actualizado", p!.Nombre); Assert.Equal("8090001234", p.Telefono);
            Assert.Equal(EstadoPiloto.Ocupado, p.Estado); Assert.True(p.EnLinea);
        });
    }

    [Theory]
    [InlineData("pilot-a2@example.test")]
    [InlineData(" PILOT-B@EXAMPLE.TEST ")]
    public async Task Duplicate_email_rolls_back_profile_and_password_and_never_echoes_password(string email)
    {
        var identity = await IdentityState(); var pilot = await PilotState();
        using var old = await Login(); using var manager = await app.LoginWebAsync("manager-a");
        using var response = await Edit(manager, Form(email, Replacement));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Replacement, await response.Content.ReadAsStringAsync());
        Assert.Equal(identity, await IdentityState()); Assert.Equal(pilot, await PilotState());
        Assert.Equal(HttpStatusCode.OK, (await old.Client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("invalid-email", Replacement, Replacement)]
    [InlineData("", Replacement, Replacement)]
    [InlineData(UpdatedEmail, "short", "short")]
    [InlineData(UpdatedEmail, Replacement, "Different-confirmation42!")]
    public async Task Invalid_inputs_make_no_partial_change_and_passwords_are_not_rendered(string email, string password, string confirmation)
    {
        var identity = await IdentityState(); var pilot = await PilotState();
        using var manager = await app.LoginWebAsync("manager-a");
        using var response = await Edit(manager, Form(email, password, confirmation));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, html); Assert.DoesNotContain(confirmation, html);
        Assert.Equal(identity, await IdentityState()); Assert.Equal(pilot, await PilotState());
    }

    [Fact]
    public async Task Password_policy_failure_after_email_update_rolls_back_the_entire_transaction()
    {
        var identity = await IdentityState(); var pilot = await PilotState();
        using var stricter = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IPasswordValidator<IdentityUser>>(new RejectReplacementPassword())));
        using var manager = stricter.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var loginPage = await manager.GetStringAsync("/Identity/Account/Login");
        using var signed = await manager.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "manager-a@example.test", ["Input.Password"] = PilotGoFactory.Password,
            ["__RequestVerificationToken"] = Csrf(loginPage)
        }));
        Assert.Equal(HttpStatusCode.Redirect, signed.StatusCode);
        using var response = await Edit(manager, Form(UpdatedEmail, Replacement));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Replacement, await response.Content.ReadAsStringAsync());
        Assert.Equal(identity, await IdentityState()); Assert.Equal(pilot, await PilotState());
        Assert.Equal(HttpStatusCode.OK, await LoginStatus("pilot-a@example.test", PilotGoFactory.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatus(UpdatedEmail, Replacement));
    }

    private sealed class RejectReplacementPassword : IPasswordValidator<IdentityUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<IdentityUser> manager, IdentityUser user, string? password)
            => Task.FromResult(password == Replacement
                ? IdentityResult.Failed(new IdentityError { Code = "TestPasswordPolicy", Description = "La contraseña de prueba fue rechazada." })
                : IdentityResult.Success);
    }

    [Fact]
    public async Task Foreign_pilot_cannot_be_selected_for_a_credential_change()
    {
        var identity = await IdentityState("pilot-b"); var pilot = await PilotState(app.PilotB);
        using var manager = await app.LoginWebAsync("manager-a");
        var form = Form(UpdatedEmail, Replacement); form["Id"] = app.PilotB.ToString();
        using var response = await Edit(manager, form);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(identity, await IdentityState("pilot-b")); Assert.Equal(pilot, await PilotState(app.PilotB));
    }

    [Theory]
    [InlineData("shop-a")]
    [InlineData("pilot-a")]
    public async Task Non_manager_roles_cannot_edit_pilot_credentials(string actor)
    {
        var identity = await IdentityState();
        using var client = await app.LoginWebAsync(actor);
        using var response = await Edit(client, Form(UpdatedEmail, Replacement));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.OriginalString);
        Assert.Equal(identity, await IdentityState());
    }

    [Fact]
    public async Task Manager_request_without_CSRF_cannot_change_credentials()
    {
        var identity = await IdentityState();
        using var manager = await app.LoginWebAsync("manager-a");
        using var response = await Edit(manager, Form(UpdatedEmail, Replacement), csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(identity, await IdentityState());
    }

    [Theory]
    [InlineData("store", true)]
    [InlineData("store", false)]
    [InlineData("tenantMembership", true)]
    [InlineData("tenantMembership", false)]
    [InlineData("otherPilot", true)]
    [InlineData("otherPilot", false)]
    [InlineData("gestorBinding", true)]
    public async Task Shared_identity_links_block_credential_takeover_even_when_the_link_is_inactive(string link, bool active)
    {
        await AddSharedLink(link, active);
        var identity = await IdentityState(); var pilot = await PilotState();
        using var manager = await app.LoginWebAsync("manager-a");
        var form = Form(UpdatedEmail, Replacement); form["PuedeEditarAcceso"] = "true";
        using var response = await Edit(manager, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Replacement, await response.Content.ReadAsStringAsync());
        Assert.Equal(identity, await IdentityState()); Assert.Equal(pilot, await PilotState());
    }

    private async Task AddSharedLink(string link, bool active)
        => await app.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            switch (link)
            {
                case "store": db.UsuariosTienda.Add(new() { IdentityUserId = "pilot-a", TiendaId = app.ShopB, Activo = active }); break;
                case "tenantMembership": db.UsuariosClienteApp.Add(new() { IdentityUserId = "pilot-a", ClienteAppId = app.TenantB, Activo = active }); break;
                case "otherPilot": db.Pilotos.Add(new() { IdentityUserId = "pilot-a", ClienteAppId = app.TenantB, Nombre = "Vínculo ajeno", Telefono = "5550000", Activo = active }); break;
                case "gestorBinding": (await db.ClientesApp.FindAsync(app.TenantB))!.GestorIdentityUserId = "pilot-a"; break;
                default: throw new ArgumentException("Vínculo QA inválido.");
            }
            await db.SaveChangesAsync();
        });

    [Theory]
    [InlineData("Cliente")]
    [InlineData("Gestor")]
    [InlineData("SuperAdmin")]
    public async Task Privileged_or_mixed_global_roles_cannot_be_taken_over_from_pilot_edit(string role)
    {
        await app.InScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new(role))).Succeeded);
            var users = services.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync("pilot-a"))!, role)).Succeeded);
        });
        var identity = await IdentityState();
        using var manager = await app.LoginWebAsync("manager-a");
        using var response = await Edit(manager, Form(UpdatedEmail, Replacement));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(identity, await IdentityState());
    }

    [Fact]
    public async Task Profile_only_edit_keeps_a_legacy_username_and_its_existing_session()
    {
        await app.InScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.SetUserNameAsync((await users.FindByIdAsync("pilot-a"))!, "legacy-pilot-a")).Succeeded);
        });
        var identity = await IdentityState();
        using var old = await Login();
        using var manager = await app.LoginWebAsync("manager-a");
        using var response = await Edit(manager, Form());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(identity, await IdentityState());
        Assert.Equal(HttpStatusCode.OK, (await old.Client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Shared_credential_block_does_not_prevent_profile_only_updates()
    {
        await AddSharedLink("store", active: false);
        var identity = await IdentityState();
        using var manager = await app.LoginWebAsync("manager-a");
        using var response = await Edit(manager, Form());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(identity, await IdentityState());
        await app.InScopeAsync(async services => Assert.Equal("Piloto actualizado",
            (await services.GetRequiredService<ApplicationDbContext>().Pilotos.FindAsync(app.PilotA))!.Nombre));
    }
}
