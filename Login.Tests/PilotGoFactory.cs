using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Login.Tests;

public sealed class PilotGoFactory : WebApplicationFactory<Program>
{
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"pilotgo-tests-{Guid.NewGuid():N}.db");
    private readonly bool _useSqlServer = Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER") == "1";
    private readonly string _sqlInstance = Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER_INSTANCE") ?? "PilotGoCodexQA_20261109";
    private readonly string _sqlDatabase = $"PilotGoTest_{Guid.NewGuid():N}";
    private readonly bool _preserveExistingSqlDatabase;
    private bool _cleanupStarted;
    public const string Password = "Test-only-Password42!";
    public const string JwtKey = "PilotGo_Integration_Tests_Only_Key_12345678901234567890";
    public TestClock Clock { get; } = new();
    public int TenantA, TenantB, ShopA, ShopA2, ShopB, CustomerA, CustomerA2, CustomerB, PilotA, PilotA2, PilotB;

    public PilotGoFactory(string? existingSqlDatabase = null)
    {
        if (existingSqlDatabase is null) return;
        if (!_useSqlServer || !Regex.IsMatch(existingSqlDatabase, "^PilotGoTest_[a-f0-9]{32}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Sólo se puede adjuntar una base SQL Server QA con nombre PilotGoTest_<GUID>.", nameof(existingSqlDatabase));
        _sqlDatabase = existingSqlDatabase;
        _preserveExistingSqlDatabase = true;
    }

    public bool PreservesExistingSqlDatabase => _preserveExistingSqlDatabase;

    // Adjuntar nunca crea esquema, migra, crea usuarios ni ejecuta seeds. Los
    // identificadores se leen de la base conservada y se valida su contexto QA.
    public async Task LoadExistingQaAsync()
    {
        if (!_preserveExistingSqlDatabase)
            throw new InvalidOperationException("Este factory no está adjunto a una base QA existente.");
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.GetDbConnection().Database != _sqlDatabase || !await db.Database.CanConnectAsync())
            throw new InvalidOperationException("La base QA solicitada no existe o no está disponible; no se creará otra.");
        var expectedUsers = new[] { "shop-a", "shop-b", "multi", "pilot-a", "pilot-a2", "pilot-b", "manager-a" };
        var users = await db.Users.AsNoTracking().Where(u => expectedUsers.Contains(u.Id))
            .Select(u => new { u.Id, u.Email }).ToListAsync();
        if (users.Count != expectedUsers.Length || users.Any(u => u.Email != $"{u.Id}@example.test"))
            throw new InvalidOperationException("La base no contiene las identidades ficticias QA esperadas.");

        TenantA = await db.ClientesApp.AsNoTracking().Where(t => t.GestorIdentityUserId == "manager-a").Select(t => t.Id).SingleAsync();
        ShopA = await db.UsuariosTienda.AsNoTracking().Where(u => u.IdentityUserId == "shop-a" && u.Activo)
            .Select(u => u.TiendaId).SingleAsync();
        ShopB = await db.UsuariosTienda.AsNoTracking().Where(u => u.IdentityUserId == "shop-b" && u.Activo)
            .Select(u => u.TiendaId).SingleAsync();
        TenantB = await db.Tiendas.AsNoTracking().Where(t => t.Id == ShopB).Select(t => t.ClienteAppId).SingleAsync();
        ShopA2 = await db.UsuariosTienda.AsNoTracking().Where(u => u.IdentityUserId == "multi" && u.Activo
                && u.TiendaId != ShopA && u.Tienda.ClienteAppId == TenantA).Select(u => u.TiendaId).SingleAsync();
        PilotA = await db.Pilotos.AsNoTracking().Where(p => p.IdentityUserId == "pilot-a" && p.ClienteAppId == TenantA).Select(p => p.Id).SingleAsync();
        PilotA2 = await db.Pilotos.AsNoTracking().Where(p => p.IdentityUserId == "pilot-a2" && p.ClienteAppId == TenantA).Select(p => p.Id).SingleAsync();
        PilotB = await db.Pilotos.AsNoTracking().Where(p => p.IdentityUserId == "pilot-b" && p.ClienteAppId == TenantB).Select(p => p.Id).SingleAsync();
        if (TenantA == TenantB || !await db.Tiendas.AsNoTracking().AnyAsync(t => t.Id == ShopA && t.ClienteAppId == TenantA))
            throw new InvalidOperationException("Las pertenencias de las empresas QA no coinciden.");
        CustomerA = await db.UsuariosFinales.AsNoTracking().Where(c => c.TiendaId == ShopA).OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
        CustomerA2 = await db.UsuariosFinales.AsNoTracking().Where(c => c.TiendaId == ShopA2).OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
        CustomerB = await db.UsuariosFinales.AsNoTracking().Where(c => c.TiendaId == ShopB).OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (!Regex.IsMatch(_sqlInstance, "^[A-Za-z0-9_-]{1,64}$"))
            throw new InvalidOperationException("Nombre de instancia LocalDB de pruebas inválido.");
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.ConfigureTestServices(services =>
        {
            // Las pruebas no dependen del EventLog de Windows ni de permisos
            // del sistema operativo para comprobar un error de base de datos.
            services.AddLogging(logging => logging.ClearProviders());
            // Cookies/CSRF reales con claves aisladas por host de pruebas;
            // nunca escribir ni reutilizar las claves del usuario o producción.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // La emisión/refresh y la validación observan el mismo reloj
                // controlado. Conserva expiración, not-before y tolerancia reales.
                options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
                {
                    var now = Clock.GetUtcNow().UtcDateTime;
                    return expires.HasValue && expires.Value >= now.Subtract(parameters.ClockSkew)
                        && (!notBefore.HasValue || notBefore.Value <= now.Add(parameters.ClockSkew));
                };
            });
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                if (_useSqlServer)
                    options.UseSqlServer($"Server=(localdb)\\{_sqlInstance};Database={_sqlDatabase};Integrated Security=true;TrustServerCertificate=true",
                        sql => sql.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter));
                else
                    options.UseSqlite($"Data Source={_database};Default Timeout=30;Pooling=False");
            });
            services.RemoveAll<IGoogleMapsService>();
            services.AddSingleton<IGoogleMapsService, NoMaps>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task SeedAsync(string? migration = null)
    {
        if (_preserveExistingSqlDatabase)
            throw new InvalidOperationException("No se permite ejecutar seed, EnsureCreated o migraciones en una base QA adjunta.");
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (migration is null) await db.Database.EnsureCreatedAsync();
        else await db.GetService<IMigrator>().MigrateAsync(migration);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Cliente", "Piloto", "Gestor" })
            Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var (id, role) in new[] { ("shop-a", "Cliente"), ("shop-b", "Cliente"), ("multi", "Cliente"), ("pilot-a", "Piloto"), ("pilot-a2", "Piloto"), ("pilot-b", "Piloto"), ("manager-a", "Gestor") })
        {
            var user = new IdentityUser { Id = id, UserName = $"{id}@example.test", Email = $"{id}@example.test" };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        var plan = new Plan { Nombre = "QA", MaxTiendas = 10, MaxPilotos = 10, MaxOrdenesPorMes = 1000 };
        var a = new ClienteApp { NombreComercial = "Tenant A", Plan = plan, GestorIdentityUserId = "manager-a" };
        var b = new ClienteApp { NombreComercial = "Tenant B", Plan = plan };
        var sa = new Tienda { ClienteApp = a, Nombre = "A1", Direccion = "Calle A", AutoAsignacion = true };
        var sa2 = new Tienda { ClienteApp = a, Nombre = "A2", Direccion = "Calle A2" };
        var sb = new Tienda { ClienteApp = b, Nombre = "B1", Direccion = "Calle B", AutoAsignacion = true };
        var ca = new UsuarioFinal { Tienda = sa, Nombre = "Destino A", DireccionUbicacion = "Destino privado A", Telefono = "5550001" };
        var ca2 = new UsuarioFinal { Tienda = sa2, Nombre = "Destino A2", DireccionUbicacion = "Destino privado A2", Telefono = "5550002" };
        var cb = new UsuarioFinal { Tienda = sb, Nombre = "Destino B", DireccionUbicacion = "Destino privado B", Telefono = "5550003" };
        var pa = new Piloto { ClienteApp = a, Nombre = "Piloto A", IdentityUserId = "pilot-a", EnLinea = true };
        var pa2 = new Piloto { ClienteApp = a, Nombre = "Piloto A2", IdentityUserId = "pilot-a2", EnLinea = true };
        var pb = new Piloto { ClienteApp = b, Nombre = "Piloto B", IdentityUserId = "pilot-b", EnLinea = true };
        db.AddRange(ca, ca2, cb, pa, pa2, pb);
        db.UsuariosTienda.AddRange(
            new UsuarioTienda { IdentityUserId = "shop-a", Tienda = sa, Activo = true },
            new UsuarioTienda { IdentityUserId = "shop-b", Tienda = sb, Activo = true },
            new UsuarioTienda { IdentityUserId = "multi", Tienda = sa, Activo = true },
            new UsuarioTienda { IdentityUserId = "multi", Tienda = sa2, Activo = true });
        await db.SaveChangesAsync();
        (TenantA, TenantB, ShopA, ShopA2, ShopB) = (a.Id, b.Id, sa.Id, sa2.Id, sb.Id);
        (CustomerA, CustomerA2, CustomerB, PilotA, PilotA2, PilotB) = (ca.Id, ca2.Id, cb.Id, pa.Id, pa2.Id, pb.Id);
    }

    public async Task<HttpClient> LoginAsync(string id)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = $"{id}@example.test", password = Password });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return client;
    }

    public async Task<HttpClient> LoginWebAsync(string id)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/Identity/Account/Login");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "El login web debe emitir un token CSRF.");
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = $"{id}@example.test", ["Input.Password"] = Password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("Login", response.Headers.Location?.OriginalString ?? "");
        return client;
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _useSqlServer && !_preserveExistingSqlDatabase && !_cleanupStarted)
        {
            _cleanupStarted = true;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Nunca usar DefaultConnection ni una base existente para las pruebas.
            if (db.Database.GetDbConnection().Database != _sqlDatabase || !_sqlDatabase.StartsWith("PilotGoTest_"))
                throw new InvalidOperationException("La base de pruebas no coincide.");
            db.Database.EnsureDeleted();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            File.Delete(_database);
        }
    }

    private sealed class NoMaps : IGoogleMapsService
    {
        public string ApiKey => "";
        public string BrowserApiKey => "";
        public Task<(double Lat, double Lng)?> GeocodificarAsync(string direccion) => Task.FromResult<(double, double)?>(null);
        public Task<double?> DistanciaRutaKmAsync(double latOrigen, double lngOrigen, double latDestino, double lngDestino) => Task.FromResult<double?>(null);
        public double CalcularDistanciaKm(double lat1, double lon1, double lat2, double lon2) => 0;
    }

    public sealed class TestClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan amount) => _offset += amount;
    }
}
