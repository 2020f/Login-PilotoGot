using Login.Data;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Login.Application.Services;

/// <summary>Límites efectivos del tenant. Cero significa ilimitado; los desactivados no consumen cuota.</summary>
public sealed class QuotaPolicyService(ApplicationDbContext db, TimeProvider? clock = null)
{
    public sealed record EffectiveQuota(int MaxTiendas, int MaxPilotos, int MaxOrdenesMes, int MaxUsuarios);

    public async Task<EffectiveQuota> ResolveAsync(int tenantId)
    {
        var tenant = await db.ClientesApp.AsNoTracking().Where(c => c.Id == tenantId)
            .Select(c => new { c.Plan.MaxTiendas, Pilotos = c.MaxPilotos ?? c.Plan.MaxPilotos,
                Ordenes = c.MaxOrdenesMes ?? c.Plan.MaxOrdenesPorMes, Usuarios = c.MaxUsuarios ?? 0 }).SingleAsync();
        return new(tenant.MaxTiendas, tenant.Pilotos, tenant.Ordenes, tenant.Usuarios);
    }

    // El mismo bloqueo de fila que usa la creación de órdenes, para que bajas y altas
    // no puedan pasar comprobaciones contra una fotografía de datos ya obsoleta.
    public async Task<IDbContextTransaction> BeginTenantMutationAsync(int tenantId, bool requireActive = true)
    {
        var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var count = await db.ClientesApp.Where(c => c.Id == tenantId && (!requireActive || c.Estado == EstadoClienteApp.Activo))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrdenSeq, c => c.OrdenSeq));
            if (count != 1) throw new InvalidOperationException("Empresa no disponible.");
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }

    public async Task EnsureNewStoreAsync(int tenantId, bool createsUser)
    {
        await EnsureSubscriptionAllowsNewWorkAsync(tenantId);
        var quota = await ResolveAsync(tenantId);
        Check(quota.MaxTiendas, await db.Tiendas.CountAsync(t => t.ClienteAppId == tenantId && t.Activo) + 1, "comercios");
        if (createsUser) await EnsureUsersAsync(tenantId, quota, null, 1);
    }

    public async Task EnsureNewPilotAsync(int tenantId)
    {
        await EnsureSubscriptionAllowsNewWorkAsync(tenantId);
        var quota = await ResolveAsync(tenantId);
        Check(quota.MaxPilotos, await db.Pilotos.CountAsync(t => t.ClienteAppId == tenantId && t.Activo) + 1, "pilotos");
        await EnsureUsersAsync(tenantId, quota, null, 1);
    }

    public async Task EnsureStoreReactivationAsync(int tenantId, int storeId)
    {
        await EnsureSubscriptionAllowsNewWorkAsync(tenantId);
        var quota = await ResolveAsync(tenantId);
        Check(quota.MaxTiendas, await db.Tiendas.CountAsync(t => t.ClienteAppId == tenantId && t.Activo && t.Id != storeId) + 1, "comercios");
        var members = await db.UsuariosTienda.Where(u => u.TiendaId == storeId && u.Activo).Select(u => u.IdentityUserId).ToListAsync();
        await EnsureUsersAsync(tenantId, quota, members);
    }

    public async Task EnsurePilotReactivationAsync(int tenantId, int pilotId, string userId)
    {
        await EnsureSubscriptionAllowsNewWorkAsync(tenantId);
        var quota = await ResolveAsync(tenantId);
        Check(quota.MaxPilotos, await db.Pilotos.CountAsync(t => t.ClienteAppId == tenantId && t.Activo && t.Id != pilotId) + 1, "pilotos");
        await EnsureUsersAsync(tenantId, quota, new[] { userId });
    }

    public async Task EnsureMembershipAsync(int tenantId, string userId)
    {
        await EnsureSubscriptionAllowsNewWorkAsync(tenantId);
        await EnsureUsersAsync(tenantId, await ResolveAsync(tenantId), new[] { userId });
    }

    public async Task EnsureNewUserAsync(int tenantId)
    {
        await EnsureSubscriptionAllowsNewWorkAsync(tenantId);
        await EnsureUsersAsync(tenantId, await ResolveAsync(tenantId), null, 1);
    }

    // Vencimiento comercial separado de suspensión manual: finalizar una entrega,
    // cobrar, consultar y solicitar renovación siguen disponibles.
    public async Task EnsureSubscriptionAllowsNewWorkAsync(int tenantId)
    {
        var billing = new SaasBillingService(db, this, clock ?? TimeProvider.System);
        if (await billing.IsExpiredAsync(tenantId))
            throw new InvalidOperationException("La suscripción está vencida o aún no ha iniciado. Contacta al gestor para renovar; las entregas en curso pueden finalizarse.");
    }

    public async Task<int> CountUsersAsync(int tenantId) => (await ActiveUserIdsAsync(tenantId)).Count;

    private async Task<HashSet<string>> ActiveUserIdsAsync(int tenantId)
    {
        var stores = db.UsuariosTienda.Where(u => u.Activo && u.Tienda.Activo && u.Tienda.ClienteAppId == tenantId).Select(u => u.IdentityUserId);
        var pilots = db.Pilotos.Where(p => p.Activo && p.ClienteAppId == tenantId).Select(p => p.IdentityUserId);
        var managers = db.ClientesApp.Where(c => c.Id == tenantId && c.GestorIdentityUserId != null).Select(c => c.GestorIdentityUserId!);
        return (await stores.Union(pilots).Union(managers).Distinct().ToListAsync()).ToHashSet();
    }

    private async Task EnsureUsersAsync(int tenantId, EffectiveQuota quota, IEnumerable<string>? additional, int newUsers = 0)
    {
        if (quota.MaxUsuarios == 0) return;
        var ids = await ActiveUserIdsAsync(tenantId);
        if (additional != null) ids.UnionWith(additional);
        Check(quota.MaxUsuarios, ids.Count + newUsers, "usuarios activos");
    }

    private static void Check(int limit, int used, string label)
    {
        if (limit < 0) throw new InvalidOperationException("La cuota de la empresa no está configurada correctamente.");
        if (limit > 0 && used > limit) throw new InvalidOperationException($"Límite de {label} alcanzado ({limit}).");
    }

    public async Task EnsureNoOpenStoreOrdersAsync(int tenantId, int storeId)
    {
        if (await db.OrdenesEntrega.AnyAsync(o => o.ClienteAppId == tenantId && o.TiendaId == storeId && !EstadosEntrega.Terminales.Contains(o.Estado)))
            throw new InvalidOperationException("El comercio tiene órdenes abiertas. Finalízalas antes de desactivarlo.");
    }

    public async Task EnsureNoOpenPilotOrdersAsync(int tenantId, int pilotId)
    {
        if (await db.OrdenesEntrega.AnyAsync(o => o.ClienteAppId == tenantId && o.PilotoId == pilotId && !EstadosEntrega.Terminales.Contains(o.Estado)))
            throw new InvalidOperationException("El piloto tiene órdenes abiertas. Finalízalas antes de desactivarlo.");
    }
}
