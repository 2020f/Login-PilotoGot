using System.Net;
using System.Text.RegularExpressions;
using Login.Application.Services;
using Login.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class SaasSecurityEdgeTests : IAsyncLifetime
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
    private static string Date(DateTime date) => date.ToString("yyyy-MM-dd");
    private async Task<Dictionary<string, string>> Form(string name)
    {
        var plan = 0;
        await app.InScopeAsync(async sp => plan = (await sp.GetRequiredService<ApplicationDbContext>().ClientesApp.FindAsync(app.TenantA))!.PlanId);
        return new() { ["NombreComercial"] = name, ["PlanId"] = plan.ToString(), ["FechaInicioPlan"] = Date(Today), ["EsTrial"] = "false" };
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, Dictionary<string, string> form)
    {
        var html = await client.GetStringAsync("/SuperAdmin/CrearCliente");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        form["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync("/SuperAdmin/CrearCliente", new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task New_company_cannot_forge_payment_or_next_charge_dates()
    {
        using var admin = await app.LoginWebAsync("qa-admin");
        var form = await Form("Pago inventado QA");
        form["UltimoPago"] = Date(Today); form["ProximoCobro"] = Date(Today.AddYears(1));
        form["FechaFinPlan"] = Date(Today.AddYears(1));
        var response = await Post(admin, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.ClientesApp.CountAsync());
            Assert.Empty(await db.SaasPayments.ToListAsync());
        });
    }

    [Fact]
    public async Task Non_trial_company_cannot_receive_future_coverage_without_payment()
    {
        using var admin = await app.LoginWebAsync("qa-admin");
        var form = await Form("Sin pago QA"); form["FechaFinPlan"] = Date(Today.AddYears(1));
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, form)).StatusCode);
        await app.InScopeAsync(async sp => Assert.Equal(2, await sp.GetRequiredService<ApplicationDbContext>().ClientesApp.CountAsync()));
        form.Remove("FechaFinPlan");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, form)).StatusCode);
        var tenantId = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var tenant = await db.ClientesApp.SingleAsync(x => x.NombreComercial == "Sin pago QA"); tenantId = tenant.Id;
            Assert.Equal(Today, tenant.FechaFinPlan); Assert.Null(tenant.UltimoPago);
            Assert.Null(tenant.ProximoCobro); Assert.False(tenant.EsTrial);
            Assert.Empty(await db.SaasPayments.ToListAsync());
        });
        app.Clock.Advance(TimeSpan.FromDays(1));
        await app.InScopeAsync(async sp => Assert.True(await sp.GetRequiredService<SaasBillingService>().IsExpiredAsync(tenantId)));
    }

    [Theory]
    [InlineData("FechaInicioPlan")]
    [InlineData("FechaFinPlan")]
    public async Task Extreme_dates_return_validation_and_do_not_create_company(string field)
    {
        using var admin = await app.LoginWebAsync("qa-admin");
        var form = await Form("Fecha extrema QA"); form[field] = "9999-12-31";
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, form)).StatusCode);
        await app.InScopeAsync(async sp => Assert.Equal(2, await sp.GetRequiredService<ApplicationDbContext>().ClientesApp.CountAsync()));
    }

    [Fact]
    public async Task Trial_longer_than_ninety_days_is_rejected()
    {
        using var admin = await app.LoginWebAsync("qa-admin");
        var form = await Form("Prueba excesiva QA"); form["EsTrial"] = "true"; form["FechaFinPlan"] = Date(Today.AddDays(91));
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, form)).StatusCode);
        await app.InScopeAsync(async sp => Assert.Equal(2, await sp.GetRequiredService<ApplicationDbContext>().ClientesApp.CountAsync()));
    }

    [Fact]
    public async Task Non_trial_start_in_future_does_not_create_an_inverted_unpaid_period()
    {
        using var admin = await app.LoginWebAsync("qa-admin");
        var form = await Form("Inicio futuro QA"); form["FechaInicioPlan"] = Date(Today.AddDays(1));
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, form)).StatusCode);
        await app.InScopeAsync(async sp => Assert.Equal(2, await sp.GetRequiredService<ApplicationDbContext>().ClientesApp.CountAsync()));
    }

    [Fact]
    public async Task Trial_end_before_default_start_is_rejected()
    {
        using var admin = await app.LoginWebAsync("qa-admin");
        var form = await Form("Prueba inválida QA"); form.Remove("FechaInicioPlan");
        form["EsTrial"] = "true"; form["FechaFinPlan"] = Date(Today.AddDays(-1));
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, form)).StatusCode);
        await app.InScopeAsync(async sp => Assert.Equal(2, await sp.GetRequiredService<ApplicationDbContext>().ClientesApp.CountAsync()));
    }

    [Fact]
    public async Task Valid_trial_has_real_end_and_never_creates_a_paid_receipt()
    {
        using var admin = await app.LoginWebAsync("qa-admin");
        var form = await Form("Prueba válida QA"); form["EsTrial"] = "true"; form["FechaFinPlan"] = Date(Today.AddDays(30));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, form)).StatusCode);
        var tenantId = 0;
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var tenant = await db.ClientesApp.SingleAsync(x => x.NombreComercial == "Prueba válida QA"); tenantId = tenant.Id;
            Assert.True(tenant.EsTrial); Assert.Equal(Today.AddDays(30), tenant.FechaFinPlan);
            Assert.Null(tenant.UltimoPago); Assert.Empty(await db.SaasPayments.ToListAsync());
            Assert.False(await sp.GetRequiredService<SaasBillingService>().IsExpiredAsync(tenant.Id));
        });
        app.Clock.Advance(TimeSpan.FromDays(31));
        await app.InScopeAsync(async sp => Assert.True(await sp.GetRequiredService<SaasBillingService>().IsExpiredAsync(tenantId)));
    }
}
