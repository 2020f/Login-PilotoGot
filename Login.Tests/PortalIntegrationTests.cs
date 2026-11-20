using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Envirapido.Portal.Contracts;
using Login.Application.Portal;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.ViewModels.SuperAdmin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Login.Tests;

public sealed class PortalIntegrationTests : IAsyncLifetime
{
    private const string Key="Portal_Integration_Test_Only_Server_Key_20261117_OnlyQA";
    private readonly PilotGoFactory crm=new();
    private readonly FakePortal backend=new();
    private WebApplicationFactory<Program> bridge=null!;
    public async Task InitializeAsync()
    {
        await crm.SeedAsync();
        await crm.InScopeAsync(async sp=>
        {
            var roles=sp.GetRequiredService<RoleManager<IdentityRole>>(); await roles.CreateAsync(new("SuperAdmin"));
            var users=sp.GetRequiredService<UserManager<IdentityUser>>();
            var user=new IdentityUser{Id="portal-qa-admin",UserName="portal-qa-admin@example.test",Email="portal-qa-admin@example.test"};
            Assert.True((await users.CreateAsync(user,PilotGoFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user,"SuperAdmin")).Succeeded);
        });
        bridge=crm.WithWebHostBuilder(builder=>
        {
            builder.UseSetting("PortalIntegration:Enabled","true");
            builder.UseSetting("PortalIntegration:AdminBaseUrl","https://portal-internal.example.test");
            builder.UseSetting("PortalIntegration:AdminKey",Key);
            builder.UseSetting("PortalIntegration:CatalogReadKey",Key);
            builder.ConfigureTestServices(services=>services.AddHttpClient<PortalAdminClient>().ConfigurePrimaryHttpMessageHandler(()=>backend));
        });
    }
    public Task DisposeAsync(){bridge?.Dispose();crm.Dispose();return Task.CompletedTask;}
    private async Task<HttpClient> Login(string id)
    {
        var client=bridge.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        var page=await client.GetStringAsync("/Identity/Account/Login");
        using var response=await client.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Input.Email"]=id+"@example.test",["Input.Password"]=PilotGoFactory.Password,["__RequestVerificationToken"]=Csrf(page)}));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode); return client;
    }
    private static string Csrf(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private async Task<HttpResponseMessage> Post(HttpClient client,string action,Dictionary<string,string> form)
    {
        form["__RequestVerificationToken"]=Csrf(await client.GetStringAsync("/SuperAdmin/Portal?requestId="+backend.RequestId));
        return await client.PostAsync("/SuperAdmin/Portal/"+action,new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task All_portal_admin_views_render_without_exposing_internal_secrets()
    {
        using var admin=await Login("portal-qa-admin");
        foreach(var tab in new[]{"Solicitudes","Compradores","Catálogo web","Versiones Android","Auditoría"})
        {
            using var page=await admin.GetAsync("/SuperAdmin/Portal?tab="+Uri.EscapeDataString(tab));
            Assert.Equal(HttpStatusCode.OK,page.StatusCode); var html=await page.Content.ReadAsStringAsync();
            Assert.Contains("Portal comercial",html); Assert.DoesNotContain(Key,html);
        }
        var detail=await admin.GetStringAsync("/SuperAdmin/Portal?requestId="+backend.RequestId);
        Assert.Contains("Confirmar pago externo",detail); Assert.Contains("Vigencia de la cuenta web",detail);
        Assert.DoesNotContain("<script>alert",detail); Assert.Contains("&lt;script&gt;",detail);
        Assert.All(backend.Requests,req=>{Assert.Equal(Key,req.Key);Assert.Equal("portal-qa-admin",req.Actor);Assert.False(req.HasCookie);Assert.False(req.HasAuthorization);});
    }

    [Fact]
    public async Task Private_loopback_hop_preserves_public_https_without_forwarding_browser_credentials()
    {
        using var handler=new FakePortal(); using var http=new HttpClient(handler);
        var settings=new PortalIntegrationOptions{Enabled=true,AdminBaseUrl="http://127.0.0.1:5031",AdminKey=Key};
        var client=new PortalAdminClient(http,Options.Create(settings));
        await client.GetAsync<PortalAdminSummary>("summary","portal-qa-admin");
        Assert.Equal("https",Assert.Single(handler.Requests).ForwardedProto);
        settings.AdminBaseUrl="https://portal-private.example.test";
        await client.GetAsync<PortalAdminSummary>("summary","portal-qa-admin");
        Assert.Null(handler.Requests.Last().ForwardedProto);
        Assert.All(handler.Requests,p=>{Assert.False(p.HasCookie);Assert.False(p.HasAuthorization);});
        settings.AdminBaseUrl="http://remote.example.test";
        await Assert.ThrowsAsync<PortalBridgeException>(()=>client.GetAsync<PortalAdminSummary>("summary","portal-qa-admin"));
        Assert.Equal(2,handler.Requests.Count);
    }

    [Fact]
    public async Task Fully_paid_request_does_not_offer_a_second_payment_confirmation()
    {
        backend.FullyPaid=true; using var admin=await Login("portal-qa-admin");
        var html=await admin.GetStringAsync("/SuperAdmin/Portal?requestId="+backend.RequestId);
        Assert.Contains("ya está confirmado",WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Confirmar pago recibido</button>",html);
        Assert.Contains("Reportes por revisar",html);
        Assert.DoesNotContain(backend.Requests,r=>r.Method=="POST");
    }

    [Fact]
    public async Task Anonymous_and_company_manager_cannot_reach_portal_administration()
    {
        using var anonymous=bridge.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        Assert.Equal(HttpStatusCode.Redirect,(await anonymous.GetAsync("/SuperAdmin/Portal")).StatusCode);
        using var manager=await Login("manager-a");
        using var denied=await manager.GetAsync("/SuperAdmin/Portal");
        Assert.Equal(HttpStatusCode.Redirect,denied.StatusCode); Assert.Contains("AccessDenied",denied.Headers.Location!.ToString());
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task Payment_requires_csrf_and_explicit_manual_confirmation_and_does_not_write_CRM_finances()
    {
        using var admin=await Login("portal-qa-admin");
        var form=new Dictionary<string,string>{["requestId"]=backend.RequestId.ToString(),["amount"]="100.00",["paidOn"]="2026-11-17",["method"]="Transferencia",["reference"]="QA-PORTAL-ONLY",["reason"]="Pago ficticio verificado",["version"]="request-v1",["operationId"]=Guid.NewGuid().ToString(),["confirmed"]="true"};
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/SuperAdmin/Portal/ConfirmarPago",new FormUrlEncodedContent(form))).StatusCode);
        form["confirmed"]="false"; Assert.Equal(HttpStatusCode.Redirect,(await Post(admin,"ConfirmarPago",form)).StatusCode);
        Assert.DoesNotContain(backend.Requests,r=>r.Method=="POST");
        form["confirmed"]="true";
        Assert.Equal(HttpStatusCode.Redirect,(await Post(admin,"ConfirmarPago",form)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await Post(admin,"ConfirmarPago",form)).StatusCode);
        var posts=backend.Requests.Where(r=>r.Method=="POST").ToArray(); Assert.Equal(2,posts.Length);
        Assert.All(posts,p=>Assert.Equal(form["operationId"],p.OperationId));
        await crm.InScopeAsync(async sp=>
        {
            var db=sp.GetRequiredService<ApplicationDbContext>(); Assert.Empty(await db.SaasPayments.ToListAsync());
            Assert.Empty(await db.SaasInvoices.ToListAsync()); Assert.Empty(await db.OrdenesEntrega.ToListAsync());
            Assert.All(await db.ClientesApp.ToListAsync(),c=>{Assert.Null(c.FechaFinPlan);Assert.Null(c.UltimoPago);});
        });
    }

    [Fact]
    public async Task Portal_outage_is_reported_safely_and_existing_CRM_pages_still_work()
    {
        backend.Unavailable=true; using var admin=await Login("portal-qa-admin");
        var html=await admin.GetStringAsync("/SuperAdmin/Portal");
        Assert.Contains("no est",html); Assert.DoesNotContain("PRIVATE_STACK",html); Assert.DoesNotContain(Key,html);
        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("/SuperAdmin/Dashboard")).StatusCode);
    }

    [Fact]
    public async Task Catalog_requires_separate_server_key_and_projects_only_active_plan_fields()
    {
        await crm.InScopeAsync(async sp=>{var db=sp.GetRequiredService<ApplicationDbContext>();db.Planes.Add(new Plan{Nombre="No publicar",Activo=false,PrecioMensual=99});await db.SaveChangesAsync();});
        using var admin=await Login("portal-qa-admin");
        Assert.Equal(HttpStatusCode.Unauthorized,(await admin.GetAsync("/api/internal/portal/catalog")).StatusCode);
        using var request=new HttpRequestMessage(HttpMethod.Get,"/api/internal/portal/catalog"); request.Headers.Add("X-Envirapido-Catalog-Key",Key);
        using var response=await admin.SendAsync(request); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var catalog=await response.Content.ReadFromJsonAsync<CrmPortalCatalog>(); Assert.Single(catalog!.Plans);
        var raw=await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("ClienteApp",raw,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No publicar",raw); Assert.DoesNotContain("manager-a",raw); Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task Company_expiry_does_not_override_the_server_authenticated_catalog()
    {
        using var manager=await Login("manager-a");
        await crm.InScopeAsync(async sp=>{var db=sp.GetRequiredService<ApplicationDbContext>();(await db.ClientesApp.FindAsync(crm.TenantA))!.Estado=EstadoClienteApp.Suspendido;await db.SaveChangesAsync();});
        using var request=new HttpRequestMessage(HttpMethod.Get,"/api/internal/portal/catalog");request.Headers.Add("X-Envirapido-Catalog-Key",Key);
        using var response=await manager.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.NotEmpty((await response.Content.ReadFromJsonAsync<CrmPortalCatalog>())!.Plans);
    }

    private sealed class FakePortal : HttpMessageHandler
    {
        public Guid RequestId{get;}=Guid.NewGuid(); private Guid UserId{get;}=Guid.NewGuid();
        public bool Unavailable{get;set;}
        public bool FullyPaid{get;set;}
        public List<(string Method,string Path,string Key,string Actor,string? OperationId,bool HasCookie,bool HasAuthorization,string? ForwardedProto)> Requests{get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests.Add((request.Method.Method,request.RequestUri!.AbsolutePath,request.Headers.GetValues("X-Envirapido-Portal-Key").Single(),request.Headers.GetValues("X-Envirapido-Operator").Single(),request.Headers.TryGetValues("Idempotency-Key",out var key)?key.Single():null,request.Headers.Contains("Cookie"),request.Headers.Contains("Authorization"),request.Headers.TryGetValues("X-Forwarded-Proto",out var proto)?proto.Single():null));
            if(Unavailable)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=JsonContent.Create(new{error="PRIVATE_STACK"})});
            var profile=new PortalProfileInput("Comprador QA","Empresa web QA","5550001","Distrito Nacional","Santo Domingo","Mensajería",Notes:"<script>alert('QA')</script>");
            var plan=new PortalPlanDto("plan-qa","Plan QA", "Descripción QA",100m,"DOP",0,10,1000,"plan-v1",false);
            var order=new PortalRequestDto(RequestId,DateTimeOffset.UtcNow,plan,FullyPaid?"Approved":"Submitted",null,FullyPaid?"Confirmed":"NotReported",FullyPaid?100:0,FullyPaid?0:100,"request-v1");
            var user=new PortalAdminUserDto(UserId,"buyer-portal@example.test",true,"Active",profile.BusinessName,DateTimeOffset.UtcNow,"user-v1");
            object body=request.Method==HttpMethod.Post?new{ok=true}:request.RequestUri.AbsolutePath.Split('/').Last() switch
            {
                "summary"=>new PortalAdminSummary(1,1,1,1,0,0,0),
                "users"=>new PortalPage<PortalAdminUserDto>([user],1,1,20),
                "requests"=>new PortalPage<PortalAdminRequestDto>([new(order,UserId,user.Email,user.BusinessName)],1,1,20),
                "catalog"=>new PortalCatalogAdminResult([new(plan,false,0,null)]),
                "artifacts"=>new PortalArtifactListDto([],null,true,false),
                "audit"=>new PortalPage<PortalAuditDto>([],0,1,20),
                _=>new PortalAdminRequestDetail(order,user,profile,[],[],[],null)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(body,body.GetType())});
        }
    }
}
