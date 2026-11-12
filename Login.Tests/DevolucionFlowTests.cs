using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class DevolucionFlowTests : IAsyncLifetime
{
    private readonly PilotGoFactory _app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private OperationsActor Gestor => new(_app.TenantA, "manager-a");
    private OperationsActor Piloto => new(_app.TenantA, "pilot-a", _app.PilotA);
    private static string Key() => Guid.NewGuid().ToString("N");
    public Task InitializeAsync() => _app.SeedAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task<T> OperarAsync<T>(Func<EnvirapidoOperationsService, Task<object>> operation)
    {
        T result = default!;
        await _app.InScopeAsync(async sp => {
            var value = await operation(sp.GetRequiredService<EnvirapidoOperationsService>());
            result = JsonSerializer.SerializeToElement(value, Json).Deserialize<T>(Json)!;
        });
        return result;
    }

    private async Task<OrdenCreadaResult> CrearAsync(int cantidad = 1, bool recoger = true)
    {
        OrdenCreadaResult result = null!;
        await _app.InScopeAsync(async sp => {
            var svc = sp.GetRequiredService<IOrdenService>();
            result = await svc.CrearOrdenAsync(_app.TenantA, _app.ShopA, _app.CustomerA,
                operacionId: Key(), destinatarios: Enumerable.Range(0, cantidad).Select(i => new OrdenDestinatarioRequest(_app.CustomerA, $"Parada {i}")).ToArray());
            if (recoger) {
                await svc.AsignarPilotoAsync(result.CodigoB, _app.PilotA, "manager-a", "Gestor", result.OrdenId);
                await svc.ConfirmarRecolectaAsync(result.CodigoB, "pilot-a", "Piloto", result.OrdenId, Key());
            }
        });
        return result;
    }

    private Task<DevolucionOperativaDto> SolicitarAsync(OrdenCreadaResult orden, int index, string tipo = "Devolucion")
        => OperarAsync<DevolucionOperativaDto>(svc => svc.SolicitarDevolucionAsync(Piloto,
            new(orden.OrdenId, orden.Paradas[index].ParadaId, tipo, "Destinatario ausente", Key())));

    private async Task<DevolucionOperativaDto> AutorizarAsync(DevolucionOperativaDto caso)
        => await OperarAsync<DevolucionOperativaDto>(svc => svc.DecidirDevolucionAsync(Gestor, caso.Id,
            new("Autorizar", "Gestor revisó la solicitud", caso.Version, Key())));

    private async Task<DevolucionOperativaDto> RetornarYRecibirAsync(DevolucionOperativaDto caso)
    {
        var iniciado = await OperarAsync<DevolucionOperativaDto>(svc => svc.IniciarRetornoAsync(Piloto, caso.Id, new(caso.Version, Key())));
        return await OperarAsync<DevolucionOperativaDto>(svc => svc.RecibirDevolucionAsync(Gestor, caso.Id,
            new(_app.ShopA, "Paquete recibido físicamente en origen", iniciado.Version, Key())));
    }

    [Fact]
    public async Task Return_requires_authorization_and_receipt_and_never_consumes_C_as_delivered()
    {
        var orden = await CrearAsync();
        var caso = await SolicitarAsync(orden, 0);
        Assert.Equal("Pendiente", caso.EstadoParada); // Una solicitud no aplica pausa automática.
        await _app.InScopeAsync(async sp => {
            var svc = sp.GetRequiredService<EnvirapidoOperationsService>();
            await Assert.ThrowsAsync<RejectedOperationException>(() => svc.IniciarRetornoAsync(Piloto, caso.Id, new(caso.Version, Key())));
        });
        var autorizado = await AutorizarAsync(caso);
        Assert.Equal("DevolucionAutorizada", autorizado.EstadoParada);
        await _app.InScopeAsync(async sp => {
            var svc = sp.GetRequiredService<IOrdenService>();
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.ConfirmarEntregaParadaAsync(orden.OrdenId,
                orden.Paradas[0].ParadaId, orden.Paradas[0].CodigoC, "pilot-a", Key()));
        });
        var recibido = await RetornarYRecibirAsync(autorizado);
        Assert.Equal("Devuelta", recibido.EstadoParada); Assert.Equal("Recibida", recibido.Estado);
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var saved = await db.OrdenesEntrega.Include(o => o.Paradas).Include(o => o.Codigos).SingleAsync();
            Assert.Equal(EstadoOrden.Devuelta, saved.Estado);
            Assert.Null(saved.EntregadaAt); Assert.NotNull(saved.FinalizadaAt);
            Assert.Null(saved.Paradas.Single().EntregadaAt); Assert.NotNull(saved.Paradas.Single().DevueltaAt);
            var c = saved.Codigos.Single(c => c.Tipo == TipoCodigo.C_Finalizacion);
            Assert.False(c.Usado); Assert.False(c.Reservado);
            await sp.GetRequiredService<QuotaPolicyService>().EnsureNoOpenPilotOrdersAsync(_app.TenantA, _app.PilotA);
            Assert.False((await sp.GetRequiredService<IPilotoAppService>().GetEstadoAsync("pilot-a")).Estado == "Ocupado");
        });
    }

    [Fact]
    public async Task Authorized_retry_keeps_original_C_and_does_not_lift_incident_pause()
    {
        var orden = await CrearAsync(); var caso = await SolicitarAsync(orden, 0, "Reintento");
        var autorizado = await AutorizarAsync(caso);
        Assert.Equal("ReintentoAutorizado", autorizado.Estado); Assert.Equal("Pendiente", autorizado.EstadoParada);
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.IncidenciasOperativas.Add(new() { ClienteAppId = _app.TenantA, OrdenEntregaId = orden.OrdenId,
                ParadaEntregaId = orden.Paradas[0].ParadaId, Categoria = "Sin contacto", Descripcion = "Pausa aprobada",
                Efecto = EfectoIncidencia.PausarParada, Estado = EstadoIncidencia.Nueva }); await db.SaveChangesAsync();
            var svc = sp.GetRequiredService<IOrdenService>();
            var error = await Assert.ThrowsAsync<OperationsException>(() => svc.ConfirmarEntregaParadaAsync(orden.OrdenId,
                orden.Paradas[0].ParadaId, orden.Paradas[0].CodigoC, "pilot-a", Key()));
            Assert.Equal(409, error.StatusCode);
        });
        using var pilotoHttp = await _app.LoginAsync("pilot-a");
        var claveC = Key();
        var httpPausada = await pilotoHttp.PostAsJsonAsync($"/api/piloto/ordenes/{orden.OrdenId}/paradas/{orden.Paradas[0].ParadaId}/confirmar-entrega",
            new { codigoC = orden.Paradas[0].CodigoC, operacionId = claveC });
        Assert.Equal(HttpStatusCode.Conflict, httpPausada.StatusCode);
        Assert.False((await httpPausada.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("solicitudRechazada", out _));
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            (await db.IncidenciasOperativas.SingleAsync()).Estado = EstadoIncidencia.Resuelta; await db.SaveChangesAsync();
            var result = await sp.GetRequiredService<IOrdenService>().ConfirmarEntregaParadaAsync(orden.OrdenId,
                orden.Paradas[0].ParadaId, orden.Paradas[0].CodigoC, "pilot-a", claveC);
            Assert.Equal("Entregada", result.EstadoOrden);
        });
    }

    [Fact]
    public async Task Return_of_one_stop_then_final_C_produces_mixed_terminal_not_false_delivery()
    {
        var orden = await CrearAsync(2); var caso = await AutorizarAsync(await SolicitarAsync(orden, 0));
        await RetornarYRecibirAsync(caso);
        using var web = await _app.LoginWebAsync("pilot-a");
        var html = await web.GetStringAsync($"/Piloto/Detalle/{orden.OrdenId}");
        Assert.DoesNotContain($"name=\"ParadaId\" value=\"{orden.Paradas[0].ParadaId}\"", html);
        Assert.Contains($"name=\"ParadaId\" value=\"{orden.Paradas[1].ParadaId}\"", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var response = await web.PostAsync("/Piloto/ConfirmarEntrega", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["OrdenId"] = orden.OrdenId.ToString(), ["ParadaId"] = orden.Paradas[1].ParadaId.ToString(),
            ["CodigoC"] = orden.Paradas[1].CodigoC, ["OperacionId"] = Key(),
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("Detalle", response.Headers.Location!.ToString());
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var padre = await db.OrdenesEntrega.AsNoTracking().SingleAsync();
            Assert.Equal(EstadoOrden.FinalizadaConDevoluciones, padre.Estado);
            Assert.Null(padre.EntregadaAt); Assert.NotNull(padre.FinalizadaAt);
        });
    }

    [Fact]
    public async Task Cancellation_before_B_releases_codes_preserves_history_and_hides_destination_in_pilot_history()
    {
        var orden = await CrearAsync(recoger: false);
        await _app.InScopeAsync(sp => sp.GetRequiredService<IOrdenService>().AsignarPilotoAsync(orden.CodigoB,
            _app.PilotA, "manager-a", "Gestor", orden.OrdenId));
        var request = new CancelarOrdenRequest("Comercio canceló antes de recoger", Key());
        var first = await OperarAsync<JsonElement>(svc => svc.CancelarOrdenAsync(Gestor, orden.OrdenId, request));
        var replay = await OperarAsync<JsonElement>(svc => svc.CancelarOrdenAsync(Gestor, orden.OrdenId, request));
        Assert.Equal(first.GetRawText(), replay.GetRawText());
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoOrden.Cancelada, (await db.OrdenesEntrega.SingleAsync()).Estado);
            Assert.All(await db.CodigosEntrega.ToListAsync(), c => { Assert.False(c.Reservado); Assert.False(c.Usado); });
            var history = await sp.GetRequiredService<IPilotoAppService>().GetOrdenHistoricaAsync("pilot-a", orden.OrdenId);
            Assert.NotNull(history); Assert.All(history!.Paradas, p => Assert.Null(p.Direccion));
            Assert.Equal(EstadoParadaEntrega.Cancelada, (await db.ParadasEntrega.SingleAsync()).Estado);
        });
        var recogida = await CrearAsync();
        await _app.InScopeAsync(async sp => await Assert.ThrowsAsync<RejectedOperationException>(() =>
            sp.GetRequiredService<EnvirapidoOperationsService>().CancelarOrdenAsync(Gestor, recogida.OrdenId,
                new("Rechazo después de B", Key()))));
    }

    [Fact]
    public async Task Request_retries_are_idempotent_and_only_authorized_manager_can_receive_at_original_store()
    {
        var orden = await CrearAsync();
        var request = new SolicitarDevolucionRequest(orden.OrdenId, orden.Paradas[0].ParadaId,
            "Devolucion", "Destinatario ausente", Key());
        var first = await OperarAsync<DevolucionOperativaDto>(s => s.SolicitarDevolucionAsync(Piloto, request));
        var replay = await OperarAsync<DevolucionOperativaDto>(s => s.SolicitarDevolucionAsync(Piloto, request));
        Assert.Equal(first, replay);
        var autorizado = await AutorizarAsync(first);
        var iniciado = await OperarAsync<DevolucionOperativaDto>(s => s.IniciarRetornoAsync(Piloto,
            autorizado.Id, new(autorizado.Version, Key())));
        var key = Key();
        await _app.InScopeAsync(async sp => {
            var svc = sp.GetRequiredService<EnvirapidoOperationsService>();
            var wrongRole = await Assert.ThrowsAnyAsync<OperationsException>(() => svc.RecibirDevolucionAsync(Piloto,
                iniciado.Id, new(_app.ShopA, "Recibido", iniciado.Version, Key())));
            Assert.Equal(403, wrongRole.StatusCode);
            await Assert.ThrowsAsync<RejectedOperationException>(() => svc.RecibirDevolucionAsync(Gestor,
                iniciado.Id, new(_app.ShopB, "Tienda ajena", iniciado.Version, key)));
        });
        var goodRequest = new RecibirDevolucionRequest(_app.ShopA, "Origen verificó recepción física", iniciado.Version, key);
        var recibido = await OperarAsync<DevolucionOperativaDto>(s => s.RecibirDevolucionAsync(Gestor, iniciado.Id, goodRequest));
        Assert.Equal(recibido, await OperarAsync<DevolucionOperativaDto>(s => s.RecibirDevolucionAsync(Gestor, iniciado.Id, goodRequest)));
        await _app.InScopeAsync(async sp => {
            Assert.Equal(1, await sp.GetRequiredService<ApplicationDbContext>().DevolucionesOperativas.CountAsync());
            var svc = sp.GetRequiredService<EnvirapidoOperationsService>();
            var denied = await Assert.ThrowsAnyAsync<OperationsException>(() => svc.DevolucionesAsync(new(_app.TenantB, "manager-a")));
            Assert.Equal(403, denied.StatusCode);
            Assert.Empty(await svc.DevolucionesAsync(new(_app.TenantA, "pilot-a2", _app.PilotA2)));
        });
    }

    [Fact]
    public async Task Route_reservation_and_sequence_are_enforced_and_return_point_closes_route_without_fake_C()
    {
        var orden = await CrearAsync(2, recoger: false);
        var route = await OperarAsync<JsonElement>(svc => svc.CrearRutaAsync(Gestor,
            new("Ruta QA", DateOnly.FromDateTime(DateTime.UtcNow), [orden.OrdenId], _app.PilotA, null, false, Key())));
        var id = route.GetProperty("id").GetInt32();
        await _app.InScopeAsync(async sp => {
            var svc = sp.GetRequiredService<IOrdenService>();
            Assert.Empty(await svc.GetOrdenesAutoDisponiblesAsync(_app.PilotA));
            await Assert.ThrowsAsync<OperationsException>(() => svc.AsignarPilotoAsync(orden.CodigoB,
                _app.PilotA, "manager-a", "Gestor", orden.OrdenId));
        });
        await OperarAsync<JsonElement>(svc => svc.EstadoRutaAsync(Gestor, id,
            new("Iniciar", route.GetProperty("version").GetString()!, Key())));
        await _app.InScopeAsync(sp => sp.GetRequiredService<IOrdenService>().ConfirmarRecolectaAsync(orden.CodigoB,
            "pilot-a", "Piloto", orden.OrdenId, Key()));
        var caso = await AutorizarAsync(await SolicitarAsync(orden, 1));
        await _app.InScopeAsync(sp => sp.GetRequiredService<IOrdenService>().ConfirmarEntregaParadaAsync(orden.OrdenId,
            orden.Paradas[0].ParadaId, orden.Paradas[0].CodigoC, "pilot-a", Key()));
        await RetornarYRecibirAsync(caso);
        await _app.InScopeAsync(async sp => {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(EstadoRutaOperativa.Finalizada, (await db.RutasOperativas.SingleAsync()).Estado);
            Assert.False((await db.RutaOrdenesOperativas.SingleAsync()).Vigente);
            Assert.Equal(1, await db.PuntosRutaOperativos.CountAsync(p => p.Tipo == TipoPuntoRuta.Devolucion));
            Assert.Equal(1, await db.CodigosEntrega.CountAsync(c => c.Tipo == TipoCodigo.C_Finalizacion && c.Usado));
        });
    }
}
