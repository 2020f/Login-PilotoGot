using System.Text.Json;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Security;
using Envirapido.Portal.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Envirapido.Portal.Api.Services;

public interface IPortalCatalogSource { Task<CrmPortalCatalog> ReadAsync(CancellationToken ct = default); }
public sealed class PortalCatalogSource(HttpClient client, PortalOptions options) : IPortalCatalogSource
{
    public async Task<CrmPortalCatalog> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Uri.TryCreate(options.CatalogUrl, UriKind.Absolute, out var target) || !string.IsNullOrEmpty(target.UserInfo)
                || !string.IsNullOrEmpty(target.Query) || !string.IsNullOrEmpty(target.Fragment)
                || !(target.Scheme == Uri.UriSchemeHttps || target.Scheme == Uri.UriSchemeHttp && target.IsLoopback))
                throw Unavailable();
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Add("X-Envirapido-Catalog-Key", options.CatalogKey);
            // El enlace privado sigue siendo HTTP loopback, no TLS. El receptor
            // solo interpreta este protocolo lógico si reconoce la IP exacta
            // del proceso local como proxy; no se envía a HTTP remoto ni se
            // amplía la confianza del receptor o se sigue una redirección.
            if (target.Scheme == Uri.UriSchemeHttp && target.IsLoopback)
                request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1024 * 1024) throw Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream(); var bytes = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(bytes, ct)) > 0)
            { if (buffer.Length + read > 1024 * 1024) throw Unavailable(); await buffer.WriteAsync(bytes.AsMemory(0, read), ct); }
            var result = JsonSerializer.Deserialize<CrmPortalCatalog>(buffer.ToArray(), PortalDomainService.Json) ?? throw Unavailable();
            if (result.Plans is null || result.Plans.Count > 5000 || string.IsNullOrWhiteSpace(result.Version)
                || result.Plans.Select(p => p.Code).Distinct(StringComparer.Ordinal).Count() != result.Plans.Count
                || result.Plans.Any(p => p.Code.Length is < 1 or > 80 || p.Name.Length is < 1 or > 100 || p.Currency != "DOP" || p.MonthlyPrice < 0 || p.MonthlyPrice > 999999999.99m
                    || decimal.Round(p.MonthlyPrice, 2) != p.MonthlyPrice || p.MaxStores < 0 || p.MaxPilots < 0 || p.MaxOrdersPerMonth < 0)) throw Unavailable();
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { throw Unavailable(); }
    }
    private static PortalException Unavailable() => new(503, "CATALOG_UNAVAILABLE", "El catálogo no está disponible. Inténtalo nuevamente más tarde.");
}
public sealed class PortalCatalogService(PortalDbContext db, IPortalCatalogSource source, IMemoryCache cache, PortalOptions options, TimeProvider clock)
{
    public async Task<CrmPortalCatalog> SourceAsync(bool fresh, CancellationToken ct)
    {
        const string key = "portal-active-catalog";
        if (!fresh && cache.TryGetValue<CrmPortalCatalog>(key, out var saved) && saved is not null) return saved;
        var current = await source.ReadAsync(ct); cache.Set(key, current, TimeSpan.FromSeconds(30)); return current;
    }
    public static PortalPlanDto Plan(CrmPortalPlan source, PortalCatalogEntry entry) => new(source.Code, source.Name, entry.Description,
        source.MonthlyPrice, source.Currency, source.MaxStores, source.MaxPilots, source.MaxOrdersPerMonth,
        PortalSecurity.Hash(JsonSerializer.Serialize(source, PortalDomainService.Json)), entry.Featured);
    public static PortalPlanDto StoredPlan(PortalCatalogEntry entry)
    {
        var saved = string.IsNullOrEmpty(entry.LastPlanSnapshotJson) ? null : JsonSerializer.Deserialize<PortalPlanDto>(entry.LastPlanSnapshotJson, PortalDomainService.Json);
        return saved is not null ? saved with { Description = entry.Description, Featured = entry.Featured }
            : new(entry.Code, "Plan de origen no disponible", entry.Description, 0, "DOP", 0, 0, 0, "unavailable", entry.Featured);
    }
    public async Task<PortalCatalogDto> PublicAsync(CancellationToken ct)
    {
        var origin = await SourceAsync(false, ct);
        var published = await db.CatalogEntries.AsNoTracking().Where(x => x.IsPublished).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync(ct);
        var plans = published.Select(entry => (entry, source: origin.Plans.SingleOrDefault(p => p.Code == entry.Code)))
            .Where(x => x.source is not null).Select(x => Plan(x.source!, x.entry)).ToList();
        return new(plans, clock.GetUtcNow(), new(options.TermsVersion, options.PrivacyVersion));
    }
    public async Task<PortalPlanDto> ForPurchaseAsync(string code, string version, CancellationToken ct)
    {
        var origin = await SourceAsync(true, ct);
        var entry = await db.CatalogEntries.SingleOrDefaultAsync(x => x.Code == code && x.IsPublished, ct);
        var plan = origin.Plans.SingleOrDefault(x => x.Code == code);
        if (entry is null || plan is null) throw new PortalException(409, "PLAN_UNAVAILABLE", "El plan no está disponible para nuevas solicitudes.");
        var result = Plan(plan, entry);
        if (result.Version != version) throw new PortalException(409, "PLAN_CHANGED", "Las condiciones del plan cambiaron. Revisa el catálogo y vuelve a aceptarlas.");
        return result;
    }
    public async Task<IReadOnlyList<PortalAdminCatalogEntry>> AdminAsync(CancellationToken ct)
    {
        var origin = await SourceAsync(true, ct); var entries = await db.CatalogEntries.AsNoTracking().ToListAsync(ct);
        var active = origin.Plans.Select(plan =>
        {
            var entry = entries.SingleOrDefault(e => e.Code == plan.Code);
            return new PortalAdminCatalogEntry(Plan(plan, entry ?? new() { Code = plan.Code }), entry?.IsPublished ?? false, entry?.SortOrder ?? 0, entry?.Version);
        }).ToList();
        active.AddRange(entries.Where(e => !origin.Plans.Any(p => p.Code == e.Code)).Select(e => new PortalAdminCatalogEntry(StoredPlan(e), e.IsPublished, e.SortOrder, e.Version, false)));
        return active;
    }
}
