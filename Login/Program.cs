using Login.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Login.Data;


var builder = WebApplication.CreateBuilder(args);

// ===============================
// 1️⃣ MVC + Razor Pages (OBLIGATORIO PARA IDENTITY)
// ===============================
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ===============================
// 2️⃣ DbContext + SQL Server
// ===============================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ===============================
// 3️⃣ Identity (USUARIOS + ROLES)
// ===============================
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{

    options.SignIn.RequireConfirmedAccount = true;

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

// ===============================
// 4️⃣ Cookies (LOGIN)
// ===============================
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";

    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;
});

// ===============================
// APP
// ===============================


builder.Services.AddSingleton<IEmailSender, EmailSender>();



var app = builder.Build();

// ===============================
// Middleware
// ===============================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // 🔐 LOGIN
app.UseAuthorization();  // 🔐 PERMISOS

// ===============================
// RUTAS
// ===============================
app.MapRazorPages(); // 🔥 NECESARIO PARA /Identity/Account/Login

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await SeedData.SeedRolesAndAdminAsync(services);
}



app.Run();
