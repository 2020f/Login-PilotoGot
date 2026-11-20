using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Tests;
using Microsoft.Extensions.DependencyInjection;

internal static class RetiroQaSeed
{
    private static string Key() => Guid.NewGuid().ToString("N");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<object> CreateAsync(PilotGoFactory app)
    {
        if (app.PreservesExistingSqlDatabase) throw new InvalidOperationException("Los escenarios de retiros necesitan una base QA nueva.");
        object result = null!;
        await app.InScopeAsync(async sp =>
        {
            var core = sp.GetRequiredService<IOrdenService>();
            var operations = sp.GetRequiredService<EnvirapidoOperationsService>();
            var pilot = await operations.PilotAsync("pilot-a");
            async Task<OrdenCreadaResult> Create(string note, bool picked)
            {
                var order = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, note,
                    operacionId: Key(), destinatarios: [new(app.CustomerA, "Paquete ficticio uno"), new(app.CustomerA, "Paquete ficticio dos")]);
                await core.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
                if (picked) await core.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a", "Piloto", order.OrdenId, Key());
                return order;
            }
            var before = await Create("QA RETIRO ANTES DE RECOGER · sin entrega física", false);
            var after = await Create("QA TRASPASO CON CUSTODIA · sin entrega física", true);
            var incident = JsonSerializer.SerializeToElement(await operations.ReportarIncidenciaAsync(pilot,
                new("General", "QA: el gestor debe retirar o transferir esta parada sin consumir B/C.", Key(), after.OrdenId, after.Paradas[0].ParadaId)), Json);
            var legacy = await Create("QA DEVOLUCIONES LEGACY · traslado explícito a reasignación", true);
            var returns = new List<JsonElement>();
            foreach (var stop in legacy.Paradas)
                returns.Add(JsonSerializer.SerializeToElement(await operations.SolicitarDevolucionAsync(pilot,
                    new(legacy.OrdenId, stop.ParadaId, "Devolucion", "QA: paquete ficticio para comprobar traslado con custodia.", Key())), Json));
            result = new
            {
                tenantA = app.TenantA, tenantB = app.TenantB, pilotA = app.PilotA, pilotA2 = app.PilotA2,
                antesDeRecoger = before, conCustodia = after, incidenciaConCustodia = incident,
                devolucionLegacy = legacy, devolucionesAbiertas = returns,
                scope = "Fixtures locales ficticias. No modifica producción ni exige escanear para retirar/transferir."
            };
        });
        return result;
    }
}
