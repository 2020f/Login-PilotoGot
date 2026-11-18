using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Envirapido.Portal.Api;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Envirapido.Portal.Tests;

public sealed class PortalFactory(bool requireEmailConfirmation = true) : WebApplicationFactory<PortalProgram>
{
    public const string AdminKey = "Testing-Only-Portal-Admin-Key-Not-For-Production-2026";
    public const string Password = "Portal-Test-Only-Password42!";
    private readonly bool sql = Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER") == "1";
    private readonly string instance = Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER_INSTANCE") ?? "PilotGoVisualQA_20261111";
    private readonly string database = "EnvirapidoPortalTest_" + Guid.NewGuid().ToString("N");
    private readonly string sqlite = Path.Combine(Path.GetTempPath(), "EnvirapidoPortalTest_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string privateRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "EnvirapidoPortalTest_" + Guid.NewGuid().ToString("N")));
    private bool initialized;
    public MutableClock Clock { get; } = new();
    public MutableCatalog Catalog { get; } = new();
    public string DatabaseName => database;
    public string StorageRoot => privateRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (!Regex.IsMatch(instance, "^[A-Za-z0-9_-]{1,64}$")) throw new InvalidOperationException("Instancia QA inválida.");
        builder.UseEnvironment("Testing");
        builder.UseSetting("AllowedHosts", "portal.example.test;localhost;127.0.0.1");
        builder.UseSetting("ConnectionStrings:PortalConnection", $"Server=(localdb)\\{instance};Database={database};Integrated Security=true;TrustServerCertificate=true");
        builder.UseSetting("Portal:AdminKey", AdminKey);
        builder.UseSetting("Portal:CatalogKey", "Testing-Only-Portal-Catalog-Key-Not-For-Production-2026");
        builder.UseSetting("Portal:PublicBaseUrl", "https://portal.example.test");
        // Legacy confirmation/recovery tests explicitly exercise the optional
        // verification policy; the product default is manual approval.
        builder.UseSetting("Portal:RequireEmailConfirmation", requireEmailConfirmation ? "true" : "false");
        builder.UseSetting("Portal:DataProtectionDirectory", Path.Combine(privateRoot,"keys"));
        builder.UseSetting("PortalArtifacts:StoragePath", Path.Combine(privateRoot,"artifacts"));
        builder.UseSetting("PortalArtifacts:MinimumVersionCode", "8");
        builder.ConfigureTestServices(services =>
        {
            services.AddLogging(log => log.ClearProviders());
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<DbContextOptions<PortalDbContext>>();
            services.RemoveAll<PortalDbContext>();
            services.AddDbContext<PortalDbContext>(options =>
            {
                if (sql) options.UseSqlServer($"Server=(localdb)\\{instance};Database={database};Integrated Security=true;TrustServerCertificate=true",
                    settings => settings.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter));
                else options.UseSqlite($"Data Source={sqlite};Pooling=False");
            });
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IPortalCatalogSource>(); services.AddSingleton<IPortalCatalogSource>(Catalog);
        });
    }

    public async Task InitializeAsync(bool publishPlans = true)
    {
        if (initialized) throw new InvalidOperationException("La base QA ya fue inicializada.");
        await InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PortalDbContext>();
            if (db.Database.IsSqlServer() && db.Database.GetDbConnection().Database != database) throw new InvalidOperationException("Destino SQL no previsto.");
            await db.Database.EnsureCreatedAsync();
        });
        initialized = true;
        if (publishPlans)
        {
            using var admin = Admin();
            foreach (var plan in Catalog.Plans)
            {
                using var response = await PostAdminAsync(admin, "catalog", new PortalCatalogPublicationInput(plan.Code,true,"Condiciones ficticias de QA",0,false));
                response.EnsureSuccessStatusCode();
            }
        }
    }

    public HttpClient Buyer() => CreateClient(new() { BaseAddress = new Uri("https://portal.example.test"), AllowAutoRedirect = false });
    public HttpClient Admin()
    {
        var client = Buyer(); client.DefaultRequestHeaders.Add("X-Envirapido-Portal-Key", AdminKey);
        client.DefaultRequestHeaders.Add("X-Envirapido-Operator", "qa-superadmin"); return client;
    }
    public static async Task<HttpResponseMessage> PostAdminAsync(HttpClient client, string path, object body, string? operation = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,"/portal-api/internal/admin/"+path);
        request.Headers.Add("Idempotency-Key",operation ?? Guid.NewGuid().ToString()); request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public static async Task<HttpResponseMessage> PostBuyerAsync(HttpClient client, string path, object body, string? operation = null)
    {
        var csrf = await client.GetFromJsonAsync<PortalCsrf>("/portal-api/v1/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post,"/portal-api/v1/"+path);
        request.Headers.Add("X-CSRF-TOKEN",csrf!.RequestToken);
        request.Headers.Add("Idempotency-Key",operation ?? Guid.NewGuid().ToString()); request.Content=JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public async Task<PortalAccountDto> RegisterAsync(HttpClient client, string email = "buyer-a@example.test", string planCode = "plan-paid")
    {
        var catalog = await client.GetFromJsonAsync<PortalCatalogDto>("/portal-api/v1/catalog");
        var plan = Assert.Single(catalog!.Plans,p=>p.Code==planCode);
        var input = new PortalRegisterInput(email,Password,Password,plan.Code,plan.Version,
            new("Responsable QA","Empresa ficticia QA","5550000100","Distrito Nacional","Santo Domingo","Logística",2,4,500),catalog.PolicyVersions.Terms,catalog.PolicyVersions.Privacy);
        using var response = await PostBuyerAsync(client,"auth/register",input);
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<PortalAccountDto>())!;
    }
    public async Task<Uri> LatestMailLinkAsync(string email)
    {
        Uri? link=null;
        await InScopeAsync(async sp =>
        {
            var db=sp.GetRequiredService<PortalDbContext>(); var mail=sp.GetRequiredService<PortalMailService>();
            foreach(var row in await db.Outbox.AsNoTracking().Where(o=>o.SentAt==null).OrderByDescending(o=>o.CreatedAt).ToListAsync())
            {
                var message=mail.Read(row); if(message.To!=email)continue;
                var match=Regex.Match(message.Html,"href=\"([^\"]+)\"");
                if(match.Success){link=new Uri(WebUtility.HtmlDecode(match.Groups[1].Value));break;}
            }
        });
        return link ?? throw new InvalidOperationException("No se encontró el correo de QA.");
    }
    public async Task ConfirmEmailAsync(HttpClient buyer,string email)
    {
        var uri=await LatestMailLinkAsync(email);
        var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        using var response=await PostBuyerAsync(buyer,"auth/confirm-email",new PortalTokenInput(Guid.Parse(query["userId"]!),query["token"]!));
        response.EnsureSuccessStatusCode();
    }
    public async Task InScopeAsync(Func<IServiceProvider,Task> action)
    { using var scope=Services.CreateScope(); await action(scope.ServiceProvider); }

    protected override void Dispose(bool disposing)
    {
        if(disposing && initialized)
        {
            using var scope=Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            if(sql && db.Database.GetDbConnection().Database!=database)throw new InvalidOperationException("No se eliminará una base ajena a QA.");
            db.Database.EnsureDeleted(); initialized=false;
        }
        base.Dispose(disposing);
        if(disposing)
        {
            if(File.Exists(sqlite))File.Delete(sqlite);
            var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if(Path.GetDirectoryName(privateRoot)!=parent || !Regex.IsMatch(Path.GetFileName(privateRoot),"^EnvirapidoPortalTest_[a-f0-9]{32}$"))throw new InvalidOperationException("Ruta de limpieza QA no prevista.");
            if(Directory.Exists(privateRoot))
            {
                if((File.GetAttributes(privateRoot)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("No limpiar enlaces de directorio.");
                Directory.Delete(privateRoot,true);
            }
        }
    }
    public sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset now=new(2026,11,17,16,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan delta)=>now+=delta;
        public void Set(DateTimeOffset value)=>now=value;
    }
    public sealed class MutableCatalog : IPortalCatalogSource
    {
        public IReadOnlyList<CrmPortalPlan> Plans {get;set;}=[new("plan-paid","Plan QA pagado",1500m,"DOP",10,20,5000),new("plan-free","Plan QA gratuito",0m,"DOP",0,0,0)];
        public bool Available {get;set;}=true;
        public Task<CrmPortalCatalog> ReadAsync(CancellationToken ct=default)=>Available?Task.FromResult(new CrmPortalCatalog(Plans,"qa-source-v1")):throw new Envirapido.Portal.Api.Security.PortalException(503,"CATALOG_UNAVAILABLE","Catálogo QA no disponible.");
    }
}
