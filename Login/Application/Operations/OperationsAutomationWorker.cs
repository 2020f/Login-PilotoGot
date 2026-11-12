using Login.Data;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Operations;

public sealed class OperationsAutomationWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    IHostEnvironment environment, ILogger<OperationsAutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing") || !configuration.GetValue("Envirapido:AutomatizacionesHabilitadas", true)) return;
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue<int?>("Envirapido:IntervaloAutomatizacionSegundos") ?? 60, 15, 3600));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await ScanAsync(stoppingToken); }
                catch (Exception e) when (e is not OperationCanceledException)
                { logger.LogError(e, "El ciclo de automatización no estuvo disponible; se reintentará sin detener la aplicación."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        using var readScope = scopes.CreateScope();
        var db = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owners = await db.ClientesApp.AsNoTracking().Where(t => t.Estado == EstadoClienteApp.Activo
                && t.GestorIdentityUserId != null && db.ReglasIncidencia.Any(r => r.ClienteAppId == t.Id && r.Activa && r.DetectarDemora))
            .Select(t => t.GestorIdentityUserId!).ToListAsync(cancellationToken);
        foreach (var owner in owners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var tenantScope = scopes.CreateScope();
            var service = tenantScope.ServiceProvider.GetRequiredService<EnvirapidoOperationsService>();
            try { await service.DetectarDemorasAutomaticasAsync(await service.ManagerAsync(owner)); }
            catch (Exception e) when (e is not OperationCanceledException)
            { logger.LogError(e, "No se pudo completar un ciclo de detección operativa; se reintentará en el siguiente ciclo."); }
        }
    }
}
