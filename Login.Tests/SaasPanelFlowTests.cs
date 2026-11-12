using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class SaasPanelFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private DateTime Today => app.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-4)).Date;
    public async Task InitializeAsync()
    {
        await app.SeedAsync();
        await app.InScopeAsync(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new("SuperAdmin"))).Succeeded);
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            var admin = new IdentityUser { Id = "qa-admin", UserName = "qa-admin@example.test", Email = "qa-admin@example.test" };
            Assert.True((await users.CreateAsync(admin, PilotGoFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, "SuperAdmin")).Succeeded);
        });
    }
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private static string Key() => Guid.NewGuid().ToString("N");
    private static string Date(DateTime date) => date.ToString("yyyy-MM-dd");
    private async Task<int> Invoice() { var id = 0; await app.InScopeAsync(async sp => id = await sp.GetRequiredService<SaasBillingService>()
        .IssueInvoiceAsync(app.TenantA, Today, Today.AddMonths(1).AddDays(-1), Today, 100, "Mensualidad QA", "qa-admin", Key())); return id; }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string page, string action, Dictionary<string,string> form)
    {
        var html = await client.GetStringAsync(page);
        var token = Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "El formulario debe emitir CSRF real.");
        form["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(action, new FormUrlEncodedContent(form));
    }
    private Dictionary<string,string> Payment(int invoice, string amount = "100", bool confirm = true) => new()
    { ["EmpresaId"] = app.TenantA.ToString(), ["FacturaId"] = invoice.ToString(), ["Importe"] = amount,
        ["Fecha"] = Date(Today), ["Metodo"] = "Transferencia", ["Referencia"] = Key(), ["Clave"] = Key(), ["Confirmado"] = confirm.ToString() };

    [Fact]
    public async Task All_saas_pages_render_and_payment_requires_real_confirmation_csrf_and_current_date()
    {
        var id = await Invoice(); using var admin = await app.LoginWebAsync("qa-admin");
        foreach(var url in new[] {"/SuperAdmin/Dashboard", "/SuperAdmin/Facturacion", $"/SuperAdmin/Factura/{id}",
            $"/SuperAdmin/DetalleCliente/{app.TenantA}", "/SuperAdmin/Auditoria", "/SuperAdmin/Sistema", "/SuperAdmin/CrearFactura"})
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/SuperAdmin/RegistrarPago", new FormUrlEncodedContent(Payment(id)))).StatusCode);
        (await Post(admin, $"/SuperAdmin/Factura/{id}", "/SuperAdmin/RegistrarPago", Payment(id, confirm:false))).EnsureRedirect();
        var future = Payment(id); future["Fecha"] = Date(Today.AddDays(1));
        (await Post(admin, $"/SuperAdmin/Factura/{id}", "/SuperAdmin/RegistrarPago", future)).EnsureRedirect();
        await app.InScopeAsync(async sp => Assert.Empty(await sp.GetRequiredService<ApplicationDbContext>().SaasPayments.ToListAsync()));
    }

    [Fact]
    public async Task Payment_web_retries_do_not_double_charge_and_cancellation_restores_unpaid_invoice()
    {
        var id = await Invoice(); using var admin = await app.LoginWebAsync("qa-admin"); var form = Payment(id);
        (await Post(admin, $"/SuperAdmin/Factura/{id}", "/SuperAdmin/RegistrarPago", form)).EnsureRedirect();
        (await Post(admin, $"/SuperAdmin/Factura/{id}", "/SuperAdmin/RegistrarPago", form)).EnsureRedirect();
        var payment = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>(); payment = (await db.SaasPayments.SingleAsync()).Id;
            Assert.Equal(Today, (await db.ClientesApp.FindAsync(app.TenantA))!.UltimoPago);
        });
        var html = await admin.GetStringAsync($"/SuperAdmin/Factura/{id}"); Assert.Contains("Pagada", html);
        (await Post(admin, $"/SuperAdmin/Factura/{id}", "/SuperAdmin/AnularPago", new()
        { ["empresaId"] = app.TenantA.ToString(), ["pagoId"] = payment.ToString(), ["motivo"] = "Recibo equivocado QA", ["clave"] = Key() })).EnsureRedirect();
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var invoice = await db.SaasInvoices.Include(i=>i.Payments).SingleAsync();
            Assert.Equal(100m,SaasBillingService.Balance(invoice)); Assert.NotEqual("Pagada",SaasBillingService.Status(invoice,Today));
            Assert.Null((await db.ClientesApp.FindAsync(app.TenantA))!.UltimoPago);
            Assert.Equal(1,await db.SaasPayments.CountAsync()); Assert.NotNull((await db.SaasPayments.SingleAsync()).VoidedAt);
        });
    }

    [Fact]
    public async Task Company_profile_cannot_forge_payment_coverage_or_switch_plan_without_audited_flow()
    {
        using var admin = await app.LoginWebAsync("qa-admin"); var plan = 0;
        await app.InScopeAsync(async sp => plan = (await sp.GetRequiredService<ApplicationDbContext>().ClientesApp.FindAsync(app.TenantA))!.PlanId);
        var form = new Dictionary<string,string> { ["Id"] = app.TenantA.ToString(), ["NombreComercial"] = "Tenant A",
            ["PlanId"] = plan.ToString(), ["Estado"] = "1", ["UltimoPago"] = Date(Today), ["ProximoCobro"] = Date(Today.AddYears(1)),
            ["FechaFinPlan"] = Date(Today.AddYears(1)) };
        (await Post(admin,$"/SuperAdmin/EditarCliente/{app.TenantA}","/SuperAdmin/EditarCliente",form)).EnsureRedirect();
        await app.InScopeAsync(async sp => { var tenant = await sp.GetRequiredService<ApplicationDbContext>().ClientesApp.FindAsync(app.TenantA);
            Assert.Null(tenant!.UltimoPago); Assert.Null(tenant.ProximoCobro); Assert.Null(tenant.FechaFinPlan); });
    }

    [Fact]
    public async Task Expiry_blocks_new_orders_but_existing_pilot_finishes_and_other_tenant_operates()
    {
        using var shop = await app.LoginAsync("shop-a"); using var pilot = await app.LoginAsync("pilot-a"); using var other = await app.LoginAsync("shop-b");
        var creation = await shop.PostAsJsonAsync("/api/tienda/ordenes",new {usuarioFinalId=app.CustomerA}); creation.EnsureSuccessStatusCode();
        var order = await creation.Content.ReadFromJsonAsync<JsonElement>(); var id = order.GetProperty("ordenId").GetInt32();
        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{id}/aceptar",null)).EnsureSuccessStatusCode();
        await app.InScopeAsync(async sp => { var db=sp.GetRequiredService<ApplicationDbContext>(); (await db.ClientesApp.FindAsync(app.TenantA))!.FechaFinPlan=Today.AddDays(-1); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.BadRequest,(await shop.PostAsJsonAsync("/api/tienda/ordenes",new {usuarioFinalId=app.CustomerA})).StatusCode);
        (await other.PostAsJsonAsync("/api/tienda/ordenes",new {usuarioFinalId=app.CustomerB})).EnsureSuccessStatusCode();
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta",new {ordenId=id,codigoB=order.GetProperty("codigoB").GetString()})).EnsureSuccessStatusCode();
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega",new {ordenId=id,codigoC=order.GetProperty("codigoC").GetString()})).EnsureSuccessStatusCode();
        await app.InScopeAsync(async sp => Assert.Equal(EstadoOrden.Entregada,(await sp.GetRequiredService<ApplicationDbContext>().OrdenesEntrega.FindAsync(id))!.Estado));
    }
}

internal static class SaasTestResponseExtensions
{
    public static void EnsureRedirect(this HttpResponseMessage response) => Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
}
