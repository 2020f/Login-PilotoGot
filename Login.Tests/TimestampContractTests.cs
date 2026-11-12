using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Data;
using Login.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class TimestampContractTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Order_timestamps_include_UTC_in_offers_driver_store_and_every_delivery_stage()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        var create = await shop.PostAsJsonAsync("/api/tienda/ordenes", new
        {
            usuarioFinalId = _app.CustomerA, operacionId = Guid.NewGuid().ToString("N")
        });
        create.EnsureSuccessStatusCode();
        var order = (await create.Content.ReadFromJsonAsync<OrdenCreadaResult>())!;
        // El resultado de creación no transporta fechas. Leer lo persistido permite
        // detectar el Kind perdido por datetime2, también en las proyecciones DTO.
        var createdUtc = DateTime.UtcNow.AddMinutes(-30);
        await _app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.OrdenesEntrega.FindAsync(order.OrdenId))!.CreatedAt = DateTime.SpecifyKind(createdUtc, DateTimeKind.Unspecified);
            await db.SaveChangesAsync();
        });
        var offers = await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/ordenes-auto");
        Assert.Equal(createdUtc, Utc(offers.EnumerateArray().Single(), "createdAt"));
        var first = (await shop.GetFromJsonAsync<JsonElement>("/api/tienda/ordenes"))[0];
        Assert.Equal(createdUtc, Utc(first, "createdAt"));
        Assert.Equal(JsonValueKind.Null, first.GetProperty("assignedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("recolectadaAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("entregadaAt").ValueKind);

        (await pilot.PostAsync($"/api/piloto/ordenes-auto/{order.OrdenId}/aceptar", null)).EnsureSuccessStatusCode();
        var assigned = (await shop.GetFromJsonAsync<JsonElement>("/api/tienda/ordenes"))[0];
        Utc(assigned, "assignedAt");
        Assert.Equal(createdUtc, Utc((await pilot.GetFromJsonAsync<JsonElement>("/api/piloto/mis-ordenes"))[0], "createdAt"));
        Assert.Equal(createdUtc, Utc(await pilot.GetFromJsonAsync<JsonElement>($"/api/piloto/ordenes/{order.OrdenId}"), "createdAt"));
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-recolecta", new { order.OrdenId, order.CodigoB })).EnsureSuccessStatusCode();
        Utc((await shop.GetFromJsonAsync<JsonElement>("/api/tienda/ordenes"))[0], "recolectadaAt");
        (await pilot.PostAsJsonAsync("/api/piloto/confirmar-entrega", new { order.OrdenId, order.CodigoB, order.CodigoC })).EnsureSuccessStatusCode();
        var delivered = (await shop.GetFromJsonAsync<JsonElement>("/api/tienda/ordenes"))[0];
        foreach (var field in new[] { "createdAt", "assignedAt", "recolectadaAt", "entregadaAt" }) Utc(delivered, field);
        await _app.InScopeAsync(async sp =>
        {
            var saved = (await sp.GetRequiredService<ApplicationDbContext>().OrdenesEntrega.AsNoTracking().SingleAsync())!;
            Assert.Equal(DateTime.SpecifyKind(saved.AssignedAt!.Value, DateTimeKind.Utc), Utc(delivered, "assignedAt"));
            Assert.Equal(DateTime.SpecifyKind(saved.RecolectadaAt!.Value, DateTimeKind.Utc), Utc(delivered, "recolectadaAt"));
            Assert.Equal(DateTime.SpecifyKind(saved.EntregadaAt!.Value, DateTimeKind.Utc), Utc(delivered, "entregadaAt"));
        });
    }

    [Fact]
    public async Task Customer_and_GPS_JSON_also_keep_explicit_UTC_after_database_materialization()
    {
        using var shop = await _app.LoginAsync("shop-a");
        using var pilot = await _app.LoginAsync("pilot-a");
        using var manager = await _app.LoginWebAsync("manager-a");
        Utc((await shop.GetFromJsonAsync<JsonElement>("/api/tienda/usuarios-finales"))[0], "createdAt");
        (await pilot.PostAsJsonAsync("/api/piloto/ubicacion", new { latitud = 18.48, longitud = -69.91 })).EnsureSuccessStatusCode();
        var gps = await manager.GetFromJsonAsync<JsonElement>("/Supervisor/MapaData");
        var current = gps.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == _app.PilotA);
        var reported = Utc(current, "ultimaUbicacionAt");
        await _app.InScopeAsync(async sp =>
        {
            var stored = (await sp.GetRequiredService<ApplicationDbContext>().Pilotos.AsNoTracking().SingleAsync(p => p.Id == _app.PilotA)).UltimaUbicacionAt!.Value;
            Assert.Equal(DateTime.SpecifyKind(stored, DateTimeKind.Utc), reported);
        });
        Assert.Equal(JsonValueKind.Null, gps.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == _app.PilotA2)
            .GetProperty("ultimaUbicacionAt").ValueKind);
    }

    [Theory]
    [InlineData("\"2026-11-11T23:33:00Z\"")]
    [InlineData("\"2026-11-11T19:33:00-04:00\"")]
    [InlineData("\"2026-11-11T23:33:00\"")]
    [InlineData("\"2026-11-11\"")]
    public void Incoming_DateTime_semantics_remain_the_same_as_the_default_JSON_parser(string json)
    {
        var baseline = JsonSerializer.Deserialize<DateTime>(json);
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeJsonConverter());
        var result = JsonSerializer.Deserialize<DateTime>(json, options);
        Assert.Equal(baseline.Ticks, result.Ticks);
        Assert.Equal(baseline.Kind, result.Kind);
    }

    [Fact]
    public void Nullable_dates_and_DateTimeOffset_keep_their_existing_contract()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeJsonConverter());
        Assert.Equal("null", JsonSerializer.Serialize<DateTime?>(null, options));
        Assert.Null(JsonSerializer.Deserialize<DateTime?>("null", options));
        var offset = new DateTimeOffset(2026, 11, 11, 19, 33, 0, TimeSpan.FromHours(-4));
        Assert.Equal(JsonSerializer.Serialize(offset), JsonSerializer.Serialize(offset, options));
    }

    private static DateTime Utc(JsonElement row, string property)
    {
        var value = row.GetProperty(property).GetString();
        Assert.NotNull(value);
        Assert.EndsWith("Z", value, StringComparison.Ordinal);
        var date = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeKind.Utc, date.Kind);
        return date;
    }
}
