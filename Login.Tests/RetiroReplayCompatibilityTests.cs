using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class RetiroReplayCompatibilityTests : IAsyncLifetime
{
    private readonly PilotGoFactory app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Key() => Guid.NewGuid().ToString("N");
    public Task InitializeAsync() => app.SeedAsync();
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }

    [Theory]
    [InlineData("SolicitarRetiroParadas")]
    [InlineData("AsignarParadas")]
    [InlineData("CancelarParadas")]
    public async Task Previously_saved_request_replays_after_contract_extension(string kind)
    {
        OrdenCreadaResult order = null!;
        List<RevisionParadaRequest> stops = [];
        await app.InScopeAsync(async sp =>
        {
            var core = sp.GetRequiredService<IOrdenService>();
            order = await core.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
            await core.AsignarPilotoAsync(order.CodigoB, app.PilotA, "manager-a", "Gestor", order.OrdenId);
            stops = await sp.GetRequiredService<ApplicationDbContext>().ParadasEntrega
                .Where(p => p.OrdenEntregaId == order.OrdenId).Select(p => new RevisionParadaRequest(p.Id, p.RevisionAsignacion)).ToListAsync();
        });
        var operation = Key();
        const string reason = "Operación conservada antes de actualizar el contrato";
        object request = kind switch
        {
            "SolicitarRetiroParadas" => new SolicitarRetiroParadasRequest(order.OrdenId, stops, reason, operation),
            "AsignarParadas" => new AsignarParadasRequest(order.OrdenId, stops, app.PilotA2, reason, operation),
            _ => new CancelarParadasRequest(order.OrdenId, stops, reason, operation)
        };
        object oldPayload = kind switch
        {
            "SolicitarRetiroParadas" => new { ordenId = order.OrdenId, paradas = stops, motivo = reason, operacionId = operation, incidenciaId = (int?)null },
            "AsignarParadas" => new { ordenId = order.OrdenId, paradas = stops, pilotoId = app.PilotA2, motivo = reason, operacionId = operation },
            _ => new { ordenId = order.OrdenId, paradas = stops, motivo = reason, operacionId = operation }
        };
        async Task<JsonElement> Command(object body)
        {
            JsonElement value = default;
            await app.InScopeAsync(async sp =>
            {
                var service = sp.GetRequiredService<EnvirapidoOperationsService>();
                OperationsActor actor = new(app.TenantA, "manager-a");
                var response = body switch
                {
                    SolicitarRetiroParadasRequest x => await service.SolicitarRetiroParadasAsync(actor, x),
                    AsignarParadasRequest x => await service.AsignarParadasAsync(actor, x),
                    CancelarParadasRequest x => await service.CancelarParadasAsync(actor, x),
                    _ => throw new InvalidOperationException()
                };
                value = JsonSerializer.SerializeToElement(response, Json);
            });
            return value;
        }
        var first = await Command(request);
        var legacyJson = JsonSerializer.Serialize(oldPayload, Json);
        var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyJson)));
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var recorded = await db.OperacionesEnvirapido.SingleAsync(x => x.Tipo == kind && x.Clave == operation);
            // Simula exactamente el hash que emitía el contrato previo al despliegue.
            recorded.PayloadHash = legacyHash;
            await db.SaveChangesAsync();
        });
        Assert.Equal(first.GetRawText(), (await Command(request)).GetRawText());
        object different = request switch
        {
            SolicitarRetiroParadasRequest x => x with { CrearIncidencia = true },
            AsignarParadasRequest x => x with { IncidenciaId = 999 },
            CancelarParadasRequest x => x with { IncidenciaId = 999 },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(409, (await Assert.ThrowsAsync<OperationsException>(() => Command(different))).StatusCode);
        await app.InScopeAsync(async sp => Assert.Single(await sp.GetRequiredService<ApplicationDbContext>().OperacionesEnvirapido.ToListAsync()));
    }
}
