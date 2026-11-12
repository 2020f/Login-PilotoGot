using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Controllers;
using Login.Data;
using Login.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

// Entorno efímero, exclusivamente loopback, TestServer/SQL Server y cuentas ficticias.
var useBrowserMaps = args.Contains("--mapas");
var verifySeed = args.Contains("--verify-seed");
var qaPort = 5019;
var portIndex = Array.IndexOf(args, "--port");
if (portIndex >= 0 && (portIndex + 1 >= args.Length || !int.TryParse(args[portIndex + 1], out qaPort) || qaPort is < 1024 or > 65535))
    throw new ArgumentException("El puerto QA debe ser un número entre 1024 y 65535.");
string? existingDatabase = null;
var databaseIndex = Array.IndexOf(args, "--database");
if (databaseIndex >= 0)
{
    if (databaseIndex + 1 >= args.Length || args[databaseIndex + 1].StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException("--database requiere el nombre de una base QA existente.");
    existingDatabase = args[databaseIndex + 1];
}
Environment.SetEnvironmentVariable("PILOTGO_TEST_SQLSERVER", "1");
Environment.SetEnvironmentVariable("PILOTGO_TEST_SQLSERVER_INSTANCE", Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER_INSTANCE") ?? "PilotGoVisualQA_20261111");
using var factory = new PilotGoFactory(existingDatabase);
object manifest;
if (existingDatabase is null)
{
    await factory.SeedAsync();
    manifest = await QaSeed.CreateAsync(factory);
}
else
{
    await factory.LoadExistingQaAsync();
    // Sólo metadatos ya existentes. No se reinicia ninguna orden ni se emiten
    // credenciales o códigos nuevos al conectar al entorno usado por el usuario.
    manifest = new { entorno = "QA local existente; no producción", database = existingDatabase,
        modo = "adjunto; sin seed ni migraciones; conservar al cerrar", tenantA = factory.TenantA,
        tenantB = factory.TenantB, shopA = factory.ShopA, shopA2 = factory.ShopA2, shopB = factory.ShopB,
        pilotA = factory.PilotA, pilotA2 = factory.PilotA2, pilotB = factory.PilotB };
}
string qaDatabase = "";
await factory.InScopeAsync(sp =>
{
    qaDatabase = sp.GetRequiredService<ApplicationDbContext>().Database.GetDbConnection().Database;
    return Task.CompletedTask;
});
if (verifySeed)
{
    Console.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    return;
}
// El servidor de mapas permanece falso: sólo Maps JavaScript del navegador es optativo.
var browserKey = useBrowserMaps ? factory.Services.GetRequiredService<IConfiguration>()["GoogleMaps:BrowserApiKey"] ?? "" : "";
var noMaps = factory.Services.GetRequiredService<IGoogleMapsService>();
using var visual = factory.WithWebHostBuilder(web => web.ConfigureTestServices(services =>
{
    // Cookies no distinguen puertos. Dos hosts QA deben conservar sus sesiones
    // y tokens CSRF separados, aun al probarlos en el mismo navegador.
    services.ConfigureApplicationCookie(options => options.Cookie.Name = $".EnvirapidoQA.{qaPort}.Identity");
    services.AddAntiforgery(options => options.Cookie.Name = $".EnvirapidoQA.{qaPort}.Antiforgery");
    services.RemoveAll<IGoogleMapsService>();
    services.AddSingleton<IGoogleMapsService>(new QaBrowserMaps(noMaps, browserKey));
}));
using var client = visual.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
var builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.WebHost.UseUrls($"http://127.0.0.1:{qaPort}");
builder.Logging.ClearProviders();
var app = builder.Build();
app.Run(async context =>
{
    if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
    { context.Response.StatusCode = 403; return; }
    if (context.Request.Path == "/qa/environment")
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        { context.Response.StatusCode = 405; context.Response.Headers.Allow = "GET"; return; }
        await context.Response.WriteAsJsonAsync(new { qa = true, database = qaDatabase, port = qaPort,
            mode = factory.PreservesExistingSqlDatabase ? "attached-preserved" : "ephemeral-seeded",
            runtime = Environment.Version.ToString(),
            aspnetCore = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(WebApplication).Assembly.Location).ProductVersion?.Split('+')[0] });
        return;
    }
    if (context.Request.Path.StartsWithSegments("/qa/login", out var remaining))
    {
        var id = remaining.Value?.Trim('/');
        if (id is not ("manager-a" or "manager-b" or "qa-admin" or "shop-a" or "qa-shop-a2" or "shop-b" or "multi" or "pilot-a" or "pilot-a2" or "pilot-b")) { context.Response.StatusCode = 404; return; }
        var login = await client.GetAsync("/Identity/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        var post = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["Input.Email"] = $"{id}@example.test", ["Input.Password"] = PilotGoFactory.Password,
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) })
        };
        post.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0])));
        using var signed = await client.SendAsync(post);
        if (signed.StatusCode != HttpStatusCode.Redirect) { context.Response.StatusCode = 500; await context.Response.WriteAsync("QA login failed"); return; }
        foreach (var cookie in signed.Headers.GetValues("Set-Cookie")) context.Response.Headers.Append("Set-Cookie", cookie);
        context.Response.Redirect(id == "qa-admin" ? "/SuperAdmin/Dashboard" : id is "manager-a" or "manager-b" ? "/Operaciones" : id is "shop-a" or "qa-shop-a2" or "shop-b" or "multi" ? "/Tienda/Ordenes" : "/Piloto");
        return;
    }
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
    if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        // TestServer no debe intentar leer directamente un stream de Kestrel.
        // El QA solo acepta formularios y JSON pequeños; no procesa archivos.
        const int maxBody = 1024 * 1024;
        if (context.Request.ContentLength is > maxBody) { context.Response.StatusCode = 413; return; }
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) != 0)
        {
            if (body.Length + read > maxBody) { context.Response.StatusCode = 413; return; }
            await body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
        }
        request.Content = new ByteArrayContent(body.ToArray());
    }
    foreach (var header in context.Request.Headers)
        if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
            && !header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
            && !header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)
            && !header.Key.Equals("Expect", StringComparison.OrdinalIgnoreCase)
            && !request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    request.Headers.Host = context.Request.Host.Value;
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    context.Response.StatusCode = (int)response.StatusCode;
    foreach (var header in response.Headers.Concat(response.Content.Headers))
        if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) context.Response.Headers[header.Key] = header.Value.ToArray();
    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
});
Console.WriteLine((existingDatabase is null ? "QA_SEED " : "QA_ATTACHED ") + JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
Console.WriteLine($"Envirapido QA ficticio y local: http://127.0.0.1:{qaPort}/qa/login/manager-a");
await app.RunAsync();
