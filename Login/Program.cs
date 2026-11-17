using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Login.Application.Interfaces;
using Login.Application.Services;
using Login.Data;
using Login.Configuration;
using Login.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1) MVC + Razor Pages
builder.Services.AddScoped<ClienteAppEstadoFilter>(); // ✅ filtro tenant

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<ClienteAppEstadoFilter>(); // ✅ global: bloquea Gestor/Cliente/Piloto si tenant no está Activo
}).AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
    new Login.Serialization.UtcDateTimeJsonConverter()));

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

// 2) DbContext + SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sql =>
        sql.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter)));

// 3) Identity (usuarios + roles)
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    // Conservar el esquema de Identity y sus claves emitidas en .NET 8.
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
    options.Stores.MaxLengthForKeys = 0;
    // DEV: para que no te bloquee el login por confirmación
    options.SignIn.RequireConfirmedAccount = false;

    // Password (modo dev)
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;

    // Usuario
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// 3.5) JWT Bearer (para la API móvil)
var jwtKey = builder.Configuration["Jwt:Key"] ?? "PilotGo_Dev_Only_Secret_Key_ChangeMe_1234567890";
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")
    && (Encoding.UTF8.GetByteCount(jwtKey) < 32 || jwtKey.Contains("ChangeMe", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Configura Jwt__Key con una clave privada de al menos 32 bytes antes de iniciar en producción.");
builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "PilotGo",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "PilotGoMobile",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                context.Response.Headers.CacheControl = "no-store";
                return context.Response.WriteAsJsonAsync(new { error = "Inicia sesión para continuar." });
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers.CacheControl = "no-store";
                return context.Response.WriteAsJsonAsync(new { error = "No tienes autorización para esta operación." });
            },
            OnTokenValidated = async context =>
            {
                var sessions = context.HttpContext.RequestServices.GetRequiredService<MobileSessionService>();
                if (context.Principal is null || !await sessions.ValidateAsync(context.Principal))
                    context.Fail("Sesión no válida.");
            }
        };
    });

// 4) Cookies
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";

    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        { context.Response.StatusCode = 401; return context.Response.WriteAsJsonAsync(new { error = "Inicia sesión para continuar." }); }
        context.Response.Redirect(context.RedirectUri); return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        { context.Response.StatusCode = 403; return context.Response.WriteAsJsonAsync(new { error = "No tienes permiso para esta operación." }); }
        context.Response.Redirect(context.RedirectUri); return Task.CompletedTask;
    };
});

// HTTPS original sólo desde el proxy inmediato autorizado; cookies seguras.
builder.Services.AddTransportSecurity(builder.Configuration, builder.Environment);

// 5) App services
builder.Services.Configure<Login.Services.AccountEmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddTransient<Login.Services.IAccountEmailSender, EmailSender>();
builder.Services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<Login.Services.IAccountEmailSender>());
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddScoped<IOrdenService, OrdenService>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<ITiendaAppService, TiendaAppService>();
builder.Services.AddScoped<IPilotoAppService, PilotoAppService>();
builder.Services.AddScoped<MobileSessionService>();
builder.Services.AddScoped<QuotaPolicyService>();
builder.Services.AddScoped<SaasBillingService>();
builder.Services.AddScoped<Login.Application.Operations.EnvirapidoOperationsService>();
builder.Services.AddHostedService<Login.Application.Operations.OperationsAutomationWorker>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<Login.Application.Operations.RecorridosService>(client => client.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IGoogleMapsService, GoogleMapsService>();

// Limita intentos de validación de PIN por usuario, compartido entre API y web.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AccountRecovery", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("CodigosEntrega", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Demasiados intentos. Espera un minuto y vuelve a intentarlo." }, cancellationToken);
    };
});

var app = builder.Build();

// Middleware
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Rutas
app.MapRazorPages();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Seed
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    await SeedData.SeedRolesAndAdminAsync(services);
}

app.Run();

public partial class Program { }
