using System.Net;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class StatisticsSqlServerTests
{
    [SqlServerFact]
    public async Task Default_statistics_and_valid_filters_render_with_deliveries_and_correct_tenant()
    {
        using var app = new PilotGoFactory();
        await app.SeedAsync();
        await app.InScopeAsync(async sp =>
        {
            var orders = sp.GetRequiredService<IOrdenService>();
            var delivered = await orders.CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, "Estadísticas QA");
            await orders.AsignarPilotoAsync(delivered.CodigoB, app.PilotA, "manager-a", "Gestor", delivered.OrdenId);
            await orders.ConfirmarRecolectaAsync(delivered.CodigoB, "pilot-a", "Piloto", delivered.OrdenId);
            await orders.ConfirmarEntregaAsync(delivered.CodigoB, delivered.CodigoC, "pilot-a", "Piloto", delivered.OrdenId);
            await orders.CrearOrdenAsync(app.TenantA, app.ShopA2, app.CustomerA2, "Pendiente QA");
            await orders.CrearOrdenAsync(app.TenantB, app.ShopB, app.CustomerB, "Otro tenant QA");
        });
        using var manager = await app.LoginWebAsync("manager-a");
        foreach (var url in new[] { "/Supervisor/Estadisticas", $"/Supervisor/Estadisticas?tiendaId={app.ShopA}&pilotoId={app.PilotA}" })
        {
            var response = await manager.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Tiempos promedio", html);
            // Comparar opciones del filtro por identificador: el token CSRF
            // del layout puede contener las letras "B1" aleatoriamente.
            var stores = OptionValues(html, "TiendaId");
            Assert.Contains(app.ShopA.ToString(), stores);
            Assert.Contains(app.ShopA2.ToString(), stores);
            Assert.DoesNotContain(app.ShopB.ToString(), stores);
            var pilots = OptionValues(html, "PilotoId");
            Assert.Contains(app.PilotA.ToString(), pilots);
            Assert.DoesNotContain(app.PilotB.ToString(), pilots);
            Assert.Contains("dataPorDia", html);
        }
    }

    private static string[] OptionValues(string html, string selectId)
    {
        var select = Regex.Match(html, $"<select\\b[^>]*\\bid=\"{Regex.Escape(selectId)}\"[^>]*>(.*?)</select>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(select.Success, $"Debe renderizar el filtro {selectId}.");
        return Regex.Matches(select.Groups[1].Value, "<option\\b[^>]*\\bvalue=\"([^\"]*)\"", RegexOptions.IgnoreCase)
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
    }
}
