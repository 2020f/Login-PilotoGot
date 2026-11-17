using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Envirapido.Portal.Api;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Security;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Envirapido.Portal.Qa;
using Envirapido.Portal.Tests;
using Login.Application.Portal;
using Login.Data;
using Login.Domain.Entities;
using Login.Tests;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

var options = QaOptions.Parse(args);
Environment.SetEnvironmentVariable("PILOTGO_TEST_SQLSERVER", "1");
Environment.SetEnvironmentVariable("PILOTGO_TEST_SQLSERVER_INSTANCE", options.SqlInstance);
// Ambas factories crean bases con GUID y eliminan solo esas bases al cerrar.
// No hay parámetro de conexión, base existente, DefaultConnection ni modo de producción.
using var crmFactory = new PilotGoFactory();
using var portalFactory = new PortalFactory();
await crmFactory.SeedAsync();
await portalFactory.InitializeAsync(false);
portalFactory.Clock.Set(DateTimeOffset.UtcNow);
string crmDatabase = "";
await crmFactory.InScopeAsync(async services =>
{
    var db = services.GetRequiredService<ApplicationDbContext>();
    crmDatabase = db.Database.GetDbConnection().Database;
    if (!Regex.IsMatch(crmDatabase, "^PilotGoTest_[a-f0-9]{32}$")) throw new InvalidOperationException("La base CRM no es desechable QA.");
    var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
    var users = services.GetRequiredService<UserManager<IdentityUser>>();
    if (!(await roles.CreateAsync(new IdentityRole("SuperAdmin"))).Succeeded) throw new InvalidOperationException("No se creó el rol QA.");
    var admin = new IdentityUser { Id = "qa-admin", UserName = "qa-admin@example.test", Email = "qa-admin@example.test", EmailConfirmed = true };
    if (!(await users.CreateAsync(admin, PilotGoFactory.Password)).Succeeded || !(await users.AddToRoleAsync(admin, "SuperAdmin")).Succeeded)
        throw new InvalidOperationException("No se creó SuperAdmin ficticio QA.");
    var plan = await db.Planes.SingleAsync();
    plan.Nombre = "Operación QA"; plan.PrecioMensual = 1500m;
    db.Planes.Add(new Plan { Nombre = "Inicio gratuito QA", PrecioMensual = 0m, MaxTiendas = 1, MaxPilotos = 2, MaxOrdenesPorMes = 50 });
    await db.SaveChangesAsync();
});
if (!Regex.IsMatch(portalFactory.DatabaseName, "^EnvirapidoPortalTest_[a-f0-9]{32}$") || crmDatabase == portalFactory.DatabaseName)
    throw new InvalidOperationException("El portal requiere una base QA independiente.");

const string catalogKey = "Testing-Only-Portal-Catalog-Key-Not-For-Production-2026";
using var crmVisual = crmFactory.WithWebHostBuilder(web => web.ConfigureTestServices(services =>
{
    services.Configure<PortalIntegrationOptions>(p =>
    {
        p.Enabled = true; p.AdminBaseUrl = options.PortalOrigin;
        p.AdminKey = PortalFactory.AdminKey; p.CatalogReadKey = catalogKey; p.CatalogCurrency = "DOP";
    });
    services.ConfigureApplicationCookie(cookie => cookie.Cookie.Name = $"Envirapido.Crm.QA.{options.CrmPort}");
    foreach (var scheme in new[] { IdentityConstants.ApplicationScheme, IdentityConstants.ExternalScheme,
        IdentityConstants.TwoFactorRememberMeScheme, IdentityConstants.TwoFactorUserIdScheme })
        services.PostConfigure<CookieAuthenticationOptions>(scheme, cookie =>
        { cookie.Cookie.Name = $"Envirapido.Crm.QA.{options.CrmPort}.{scheme}"; cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; });
    services.PostConfigure<AntiforgeryOptions>(anti =>
    { anti.Cookie.Name = $"Envirapido.Crm.QA.Csrf.{options.CrmPort}"; anti.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; });
    services.PostConfigure<CookieTempDataProviderOptions>(cookie =>
    { cookie.Cookie.Name = $"Envirapido.Crm.QA.TempData.{options.CrmPort}"; cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; });
}));
using var crmClient = crmVisual.CreateClient(new() { BaseAddress = new Uri(options.CrmOrigin), HandleCookies = false, AllowAutoRedirect = false });
crmClient.Timeout = TimeSpan.FromMinutes(5);
await using var crmBridge = LoopbackBridge.Create(options.CrmOrigin, Path.Combine(portalFactory.StorageRoot, "spool-crm"), crmClient, async context =>
{
    if (context.Request.Path == "/qa/environment")
    {
        if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return true; }
        await context.Response.WriteAsJsonAsync(new { qa = true, service = "CRM", database = crmDatabase, portalOrigin = options.PortalOrigin }); return true;
    }
    if (!context.Request.Path.StartsWithSegments("/qa/login", out var remaining)) return false;
    var id = remaining.Value?.Trim('/');
    if (id is not ("qa-admin" or "manager-a" or "shop-a" or "pilot-a")) { context.Response.StatusCode = 404; return true; }
    await LoopbackBridge.LoginAsync(context, crmClient, id); return true;
});
await crmBridge.StartAsync();

using var portalVisual = portalFactory.WithWebHostBuilder(web =>
{
    web.UseEnvironment("Development");
    web.UseSetting("Portal:PublicBaseUrl", options.WebOrigin.GetLeftPart(UriPartial.Authority));
    web.UseSetting("Portal:CatalogUrl", options.CrmOrigin + "/api/internal/portal/catalog");
    web.UseSetting("Portal:CatalogKey", catalogKey);
    web.UseSetting("Portal:Mail:Enabled", "false");
    web.UseSetting("PortalArtifacts:MinimumVersionCode", "8");
    if (options.AndroidTools is not null)
    {
        web.UseSetting("PortalArtifacts:JavaPath", options.JavaPath);
        web.UseSetting("PortalArtifacts:ApkSignerJarPath", Path.Combine(options.AndroidTools, "lib", "apksigner.jar"));
        web.UseSetting("PortalArtifacts:Aapt2Path", Path.Combine(options.AndroidTools, "aapt2.exe"));
    }
    web.ConfigureTestServices(services =>
    {
        // Retirar el catálogo simulado de la factory: la UI utiliza HTTP al CRM real QA.
        services.RemoveAll<IPortalCatalogSource>();
        services.AddHttpClient<IPortalCatalogSource, PortalCatalogSource>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        // Correo retenido y cifrado en la propia BD; ningún mensaje sale de este equipo.
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PortalOutboxWorker)).ToArray())
            services.Remove(descriptor);
        services.RemoveAll<IPortalMailTransport>(); services.AddSingleton<IPortalMailTransport, NoExternalQaMail>();
        services.PostConfigure<CookieAuthenticationOptions>(PortalSecurity.CookieScheme, cookie =>
        { cookie.Cookie.Name = $"Envirapido.Portal.QA.{options.PortalPort}"; cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; });
        services.PostConfigure<AntiforgeryOptions>(anti =>
        { anti.Cookie.Name = $"Envirapido.Portal.QA.Csrf.{options.PortalPort}"; anti.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; });
    });
});
using var portalClient = portalVisual.CreateClient(new() { BaseAddress = new Uri(options.PortalOrigin), HandleCookies = false, AllowAutoRedirect = false });
portalClient.Timeout = TimeSpan.FromMinutes(5);
await using var portalBridge = LoopbackBridge.Create(options.PortalOrigin, Path.Combine(portalFactory.StorageRoot, "spool-portal"), portalClient, async context =>
{
    if (context.Request.Path == "/qa/environment")
    {
        if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return true; }
        await context.Response.WriteAsJsonAsync(new { qa = true, service = "Portal", database = portalFactory.DatabaseName,
            catalog = "HTTP real del CRM QA", smtp = false, webOrigin = options.WebOrigin, apkValidation = options.AndroidTools is not null }); return true;
    }
    if (context.Request.Path != "/qa/mail") return false;
    if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return true; }
    var email = context.Request.Query["email"].ToString();
    if (!Regex.IsMatch(email, "^[A-Za-z0-9._+%-]{1,100}@example\\.test$"))
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Usa únicamente una cuenta ficticia @example.test creada en este QA." }); return true; }
    using var scope = portalVisual.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    if (db.Database.GetDbConnection().Database != portalFactory.DatabaseName) throw new InvalidOperationException("Destino de outbox QA inesperado.");
    var reader = scope.ServiceProvider.GetRequiredService<PortalMailService>();
    var messages = new List<object>();
    foreach (var row in await db.Outbox.AsNoTracking().Where(o => o.SentAt == null).OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync(context.RequestAborted))
    {
        var mail = reader.Read(row); if (!mail.To.Equals(email, StringComparison.OrdinalIgnoreCase)) continue;
        var href = Regex.Match(mail.Html, "href=\"([^\"]+)\"");
        if (!href.Success || !Uri.TryCreate(WebUtility.HtmlDecode(href.Groups[1].Value), UriKind.Absolute, out var link)
            || link.GetLeftPart(UriPartial.Authority) != options.WebOrigin.GetLeftPart(UriPartial.Authority)) continue;
        messages.Add(new { mail.Subject, link = link.AbsoluteUri, row.CreatedAt });
    }
    await context.Response.WriteAsJsonAsync(new { qa = true, to = email, messages }); return true;
});
await portalBridge.StartAsync();
try
{
    // Publicación explícita, sobre el contrato admin y datos del catálogo HTTP real.
    using var admin = new HttpClient { BaseAddress = new Uri(options.PortalOrigin) };
    admin.DefaultRequestHeaders.Add("X-Envirapido-Portal-Key", PortalFactory.AdminKey);
    admin.DefaultRequestHeaders.Add("X-Envirapido-Operator", "qa-admin");
    using var catalogResponse = await admin.GetAsync("/portal-api/internal/admin/catalog");
    catalogResponse.EnsureSuccessStatusCode();
    using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync());
    foreach (var entry in catalog.RootElement.GetProperty("plans").EnumerateArray())
    {
        var code = entry.GetProperty("plan").GetProperty("code").GetString()!;
        using var published = await PortalFactory.PostAdminAsync(admin, "catalog", new PortalCatalogPublicationInput(code, true,
            "Plan y límites ficticios de QA local. Confirmación manual de pagos, cuenta portal independiente del CRM.", 0, false));
        published.EnsureSuccessStatusCode();
    }
    var publicCatalog = await admin.GetFromJsonAsync<PortalCatalogDto>("/portal-api/v1/catalog");
    if (publicCatalog?.Plans.Count != 2 || !publicCatalog.Plans.Any(p => p.MonthlyPrice == 1500m))
        throw new InvalidOperationException("El catálogo publicado no corresponde al CRM QA real.");
    var receipt = new
    {
        qa = true, generatedAt = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(),
        crm = new { url = options.CrmOrigin, database = crmDatabase, superAdmin = options.CrmOrigin + "/qa/login/qa-admin",
            portalAdmin = options.CrmOrigin + "/SuperAdmin/Portal", email = "qa-admin@example.test", password = PilotGoFactory.Password,
            manager = options.CrmOrigin + "/qa/login/manager-a", shop = options.CrmOrigin + "/qa/login/shop-a", pilot = options.CrmOrigin + "/qa/login/pilot-a" },
        portal = new { api = options.PortalOrigin, web = options.WebOrigin, database = portalFactory.DatabaseName,
            privateStorage = portalFactory.StorageRoot, catalogSource = options.CrmOrigin + "/api/internal/portal/catalog",
            smtp = "Deshabilitado; mensajes retenidos en outbox QA propio.", mailReader = options.PortalOrigin + "/qa/mail?email=portal-qa%40example.test",
            registrationExample = new { email = "portal-qa@example.test", password = PortalFactory.Password, accountAlreadyCreated = false },
            crmHref = PortalOptions.CrmLoginUrl, followCrmHref = false, apkValidationTools = options.AndroidTools is not null, minimumApkVersion = 8 },
        publishedPlans = publicCatalog.Plans,
        nextEnvironment = new Dictionary<string, string> { ["PORTAL_API_ORIGIN"] = options.PortalOrigin },
        notes = new[] { "Solo loopback; bases nuevas desechables; Ctrl+C cierra ambos servicios y elimina estas bases y archivos privados.",
            "El enlace CRM del contrato se conserva intacto: no seguirlo en QA; abrir el CRM local desde este recibo.",
            "No se instala, publica ni descarga un APK automáticamente. Las excepciones de cookies HTTP existen solo en el harness." }
    };
    var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    Directory.CreateDirectory(Path.GetDirectoryName(options.ReceiptPath)!); await File.WriteAllTextAsync(options.ReceiptPath, json);
    Console.WriteLine("PORTAL_QA_RECEIPT " + options.ReceiptPath);
    Console.WriteLine(json);
    if (!options.VerifyOnly) await Task.WhenAny(crmBridge.WaitForShutdownAsync(), portalBridge.WaitForShutdownAsync());
}
finally
{
    await portalBridge.StopAsync(); await crmBridge.StopAsync();
}

internal sealed class NoExternalQaMail : IPortalMailTransport
{
    public Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
        => throw new InvalidOperationException("Este harness nunca envía correo: consulta /qa/mail en loopback.");
}
