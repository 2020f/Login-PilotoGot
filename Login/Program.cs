using System.Text;
using Login.Application.Interfaces;
using Login.Application.Services;
using Login.Data;
using Login.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
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

// Rutas
app.MapRazorPages();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Seed
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await SeedData.SeedRolesAndAdminAsync(services);
}

app.Run();
