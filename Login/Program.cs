using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Login.Application.Interfaces;
using Login.Application.Services;
using Login.Data;
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
});

builder.Services.AddRazorPages();

// 2) DbContext + SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3) Identity (usuarios + roles)
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
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
});

// 5) App services
builder.Services.AddTransient<IEmailSender, EmailSender>(); // o Scoped
builder.Services.AddScoped<IOrdenService, OrdenService>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<ITiendaAppService, TiendaAppService>();
builder.Services.AddScoped<IPilotoAppService, PilotoAppService>();
builder.Services.AddScoped<MobileSessionService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IGoogleMapsService, GoogleMapsService>();

// Limita intentos de validación de PIN por usuario, compartido entre API y web.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
