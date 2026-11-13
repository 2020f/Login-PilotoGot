using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Login.Configuration;

public static class TransportSecurity
{
    public static IServiceCollection AddTransportSecurity(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var proxies = new HashSet<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };
        var section = configuration.GetSection("ReverseProxy:KnownProxies");
        if (section.Value is not null)
            throw new InvalidOperationException("ReverseProxy:KnownProxies debe ser una lista de IPs individuales del proxy inmediato.");
        foreach (var entry in section.GetChildren())
        {
            if (!IPAddress.TryParse(entry.Value, out var address)
                || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("ReverseProxy:KnownProxies requiere IPs individuales válidas; no admite comodines ni redes CIDR.");
            proxies.Add(address);
        }
        // Kestrel dual-stack puede representar un peer IPv4 como ::ffff:a.b.c.d.
        foreach (var address in proxies.ToArray())
            if (address.AddressFamily == AddressFamily.InterNetwork)
                proxies.Add(address.MapToIPv6());

        // PostConfigure conserva esta frontera incluso si el hosting registra
        // opciones predeterminadas de forwarded headers. No confiar en Host/For.
        services.PostConfigure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var address in proxies) options.KnownProxies.Add(address);
        });

        var policy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        foreach (var scheme in new[] { IdentityConstants.ApplicationScheme, IdentityConstants.ExternalScheme,
                     IdentityConstants.TwoFactorRememberMeScheme, IdentityConstants.TwoFactorUserIdScheme })
            services.Configure<CookieAuthenticationOptions>(scheme, options =>
            {
                options.Cookie.SecurePolicy = policy;
                options.Cookie.HttpOnly = true;
            });
        services.AddAntiforgery(options =>
        {
            options.Cookie.SecurePolicy = policy;
            options.Cookie.HttpOnly = true;
        });
        services.Configure<CookieTempDataProviderOptions>(options =>
        {
            options.Cookie.SecurePolicy = policy;
            options.Cookie.HttpOnly = true;
        });
        return services;
    }
}
