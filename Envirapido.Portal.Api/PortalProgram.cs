using System.Net;
using System.Threading.RateLimiting;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Security;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Envirapido.Portal.Api;

public partial class PortalProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = builder.Configuration.GetSection("Portal").Get<PortalOptions>() ?? new();
        var connection = builder.Configuration.GetConnectionString("PortalConnection");
        options.Validate(builder.Environment, connection);
        builder.Services.AddSingleton(options);
        builder.Services.AddDbContext<PortalDbContext>(db =>
        {
            if (!string.IsNullOrEmpty(connection)) db.UseSqlServer(connection, sql => sql.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter));
            else if (builder.Environment.IsEnvironment("Testing")) db.UseSqlite("Data Source=:memory:");
            else throw new InvalidOperationException("Configura ConnectionStrings:PortalConnection independiente.");
        });
        builder.Services.AddSingleton(TimeProvider.System);
        var protection = builder.Services.AddDataProtection().SetApplicationName("Envirapido.Portal.Isolated.v1");
        if (builder.Environment.IsEnvironment("Testing")) protection.UseEphemeralDataProtectionProvider();
        else
        {
            var directory = string.IsNullOrWhiteSpace(options.DataProtectionDirectory) ? Path.Combine(builder.Environment.ContentRootPath, ".portal-data", "keys") : options.DataProtectionDirectory;
            protection.PersistKeysToFileSystem(Directory.CreateDirectory(directory));
        }
        var secure = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        builder.Services.AddAntiforgery(anti =>
        {
            anti.HeaderName = "X-CSRF-TOKEN";
            anti.Cookie.Name = builder.Environment.IsDevelopment() ? "Envirapido.Portal.Dev.Csrf" : "__Host-Envirapido.Portal.Csrf";
            anti.Cookie.Path = "/"; anti.Cookie.HttpOnly = true; anti.Cookie.SecurePolicy = secure; anti.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.AddAuthentication(PortalSecurity.CookieScheme)
            .AddCookie(PortalSecurity.CookieScheme, cookie =>
            {
                cookie.Cookie.Name = builder.Environment.IsDevelopment() ? "Envirapido.Portal.Dev" : "__Host-Envirapido.Portal";
                cookie.Cookie.Path = "/"; cookie.Cookie.Domain = null; cookie.Cookie.HttpOnly = true; cookie.Cookie.SecurePolicy = secure;
                cookie.Cookie.SameSite = SameSiteMode.Lax; cookie.SlidingExpiration = false; cookie.EventsType = typeof(PortalCookieEvents);
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, PortalAdminAuthenticationHandler>(PortalSecurity.AdminScheme, _ => { });
        builder.Services.AddOptions<CookieAuthenticationOptions>(PortalSecurity.CookieScheme).Configure<TimeProvider>((cookie, clock) => cookie.TimeProvider = clock);
        builder.Services.AddAuthorization(auth =>
        {
            auth.AddPolicy("PortalBuyer", p => p.AddAuthenticationSchemes(PortalSecurity.CookieScheme).RequireAuthenticatedUser());
            auth.AddPolicy(PortalSecurity.AdminPolicy, p => p.AddAuthenticationSchemes(PortalSecurity.AdminScheme).RequireAuthenticatedUser().RequireClaim("portal-operator"));
        });
        builder.Services.AddScoped<PortalCookieEvents>();
        builder.Services.AddScoped<IPasswordHasher<PortalUser>, PasswordHasher<PortalUser>>();
        builder.Services.Configure<PasswordHasherOptions>(p => p.IterationCount = 210000);
        builder.Services.AddScoped<PortalDomainService>(); builder.Services.AddScoped<PortalCatalogService>();
        builder.Services.AddScoped<PortalAccessService>(); builder.Services.AddScoped<PortalAuditService>(); builder.Services.AddScoped<PortalMailService>();
        builder.Services.AddSingleton<IPortalMailTransport, PortalSmtpTransport>(); builder.Services.AddHostedService<PortalOutboxWorker>();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient<IPortalCatalogSource, PortalCatalogSource>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var proxies = new HashSet<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Loopback.MapToIPv6() };
        var configuredProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies");
        if (configuredProxies.Value is not null) throw new InvalidOperationException("KnownProxies debe ser una lista de IPs exactas.");
        foreach (var item in configuredProxies.GetChildren())
        {
            if (!IPAddress.TryParse(item.Value, out var ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) throw new InvalidOperationException("KnownProxies requiere IPs exactas válidas.");
            proxies.Add(ip); if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) proxies.Add(ip.MapToIPv6());
        }
        builder.Services.PostConfigure<ForwardedHeadersOptions>(f =>
        { f.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor; f.ForwardLimit = 1; f.KnownIPNetworks.Clear(); f.KnownProxies.Clear(); foreach (var ip in proxies) f.KnownProxies.Add(ip); });
        builder.Services.AddHttpsRedirection(h => { h.HttpsPort = 443; h.RedirectStatusCode = 307; });
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = 429;
            limiter.AddPolicy("PortalAuth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Environment.IsEnvironment("Testing") ? 10000 : 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            limiter.OnRejected = async (c, ct) => { c.HttpContext.Response.Headers.RetryAfter = "60"; await c.HttpContext.Response.WriteAsJsonAsync(new PortalError("Demasiados intentos. Espera un minuto.", "RATE_LIMITED"), ct); };
        });
        builder.Services.AddPortalArtifacts(builder.Configuration, builder.Environment);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var mediaType = context.Request.ContentType?.Split(';', 2)[0].Trim();
            var jsonRequest = string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType is not null && mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) && mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
            if (jsonRequest)
            {
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit) bodyLimit.MaxRequestBodySize = 65536;
                if (context.Request.ContentLength > 65536)
                {
                    await WriteError(context, 413, "REQUEST_TOO_LARGE", "La solicitud JSON supera el límite de 64 KiB.");
                    return;
                }
            }
            try { await next(context); }
            catch (PortalException e) { await WriteError(context, e.Status, e.Code, e.Message); }
            catch (AntiforgeryValidationException) { await WriteError(context, 400, "CSRF_INVALID", "Actualiza la página e inténtalo nuevamente."); }
            catch (DbUpdateConcurrencyException) { await WriteError(context, 409, "VERSION_CONFLICT", "Los datos cambiaron. Actualiza la vista."); }
            catch (DbUpdateException) { await WriteError(context, 409, "DATA_CONFLICT", "No se pudo guardar la operación. Actualiza los datos y reintenta la misma operación."); }
            catch (BadHttpRequestException e)
            {
                await WriteError(context, e.StatusCode == 413 ? 413 : 400, e.StatusCode == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION",
                    e.StatusCode == 413 ? "La solicitud JSON supera el límite de 64 KiB." : "La solicitud no es válida.");
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception)
            {
                app.Logger.LogError("Una operación del portal falló. TraceId {TraceId}; no se registran credenciales ni contenido de solicitudes.", context.TraceIdentifier);
                await WriteError(context, 500, "INTERNAL_ERROR", "No se pudo completar la solicitud. Inténtalo nuevamente.");
            }
        });
        app.UseForwardedHeaders(); if (!PortalOptions.Local(app.Environment)) app.UseHttpsRedirection();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.MapPortalCore(); app.MapPortalArtifacts();
        // La producción requiere migraciones explícitas. Nunca EnsureCreated,
        // Database.Migrate, seeds comerciales ni lecturas del CRM al arrancar.
        app.Run();
    }
    private static Task WriteError(HttpContext c, int status, string code, string message)
    {
        if (c.Response.HasStarted) { c.Abort(); return Task.CompletedTask; }
        c.Response.StatusCode = status; return c.Response.WriteAsJsonAsync(new PortalError(message, code), c.RequestAborted);
    }
}
