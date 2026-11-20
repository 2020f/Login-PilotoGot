using System.Text.Json;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

internal static class QaSeed
{
    private static string Key() => Guid.NewGuid().ToString("N");
    private static JsonElement Ack(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static async Task<object> CreateAsync(PilotGoFactory factory)
    {
        if (factory.PreservesExistingSqlDatabase)
            throw new InvalidOperationException("No se permite sembrar datos en una base QA adjunta.");
        object manifest = null!;
        await factory.InScopeAsync(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
            if (!(await roles.CreateAsync(new IdentityRole("SuperAdmin"))).Succeeded) throw new InvalidOperationException("No se pudo crear rol QA.");
            var users = sp.GetRequiredService<UserManager<IdentityUser>>();
            foreach (var (id, role) in new[] { ("qa-admin", "SuperAdmin"), ("qa-shop-a2", "Cliente"), ("manager-b", "Gestor") })
            {
                var user = new IdentityUser { Id = id, UserName = $"{id}@example.test", Email = $"{id}@example.test", EmailConfirmed = true };
                if (!(await users.CreateAsync(user, PilotGoFactory.Password)).Succeeded || !(await users.AddToRoleAsync(user, role)).Succeeded)
                    throw new InvalidOperationException("No se pudo crear cuenta ficticia QA.");
            }
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.UsuariosTienda.Add(new UsuarioTienda { IdentityUserId = "qa-shop-a2", TiendaId = factory.ShopA2, Activo = true, EsPrincipal = true });
            (await db.ClientesApp.FindAsync(factory.TenantB))!.GestorIdentityUserId = "manager-b";
            (await db.ClientesApp.FindAsync(factory.TenantA))!.PrecioBase = 10m;
            var stores = await db.Tiendas.OrderBy(t => t.Id).ToListAsync();
            for (var i = 0; i < stores.Count; i++)
            {
                stores[i].Telefono = $"80955501{i:00}";
                stores[i].Latitud = 18.48 + i * .004; stores[i].Longitud = -69.91 - i * .004;
            }
            var customers = await db.UsuariosFinales.OrderBy(c => c.Id).ToListAsync();
            for (var i = 0; i < customers.Count; i++)
            {
                customers[i].Latitud = 18.49 + i * .003; customers[i].Longitud = -69.92 - i * .003;
            }
            var another = new UsuarioFinal { TiendaId = factory.ShopA, Nombre = "Segundo destinatario QA",
                DireccionUbicacion = "Destino ficticio 2, Santo Domingo", Telefono = "8095550199", Latitud = 18.498, Longitud = -69.928 };
            db.UsuariosFinales.Add(another);
            var pilots = await db.Pilotos.OrderBy(p => p.Id).ToListAsync();
            for (var i = 0; i < pilots.Count; i++)
            {
                pilots[i].Telefono = $"80955502{i:00}"; pilots[i].Latitud = 18.485 + i * .003; pilots[i].Longitud = -69.905 - i * .003;
                pilots[i].UltimaUbicacionAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            var core = sp.GetRequiredService<IOrdenService>();
            var ops = sp.GetRequiredService<EnvirapidoOperationsService>();
            var manager = await ops.ManagerAsync("manager-a"); var pilot = await ops.PilotAsync("pilot-a2");
            var completed = new List<OrdenCreadaResult>();
            for (var i = 0; i < 2; i++)
            {
                var order = await core.CrearOrdenAsync(factory.TenantA, factory.ShopA, factory.CustomerA, $"Entrega finalizada QA {i + 1}", operacionId: Key());
                await core.AsignarPilotoAsync(order.CodigoB, factory.PilotA2, "manager-a", "Gestor", order.OrdenId);
                await core.ConfirmarRecolectaAsync(order.CodigoB, "pilot-a2", "Piloto", order.OrdenId, Key());
                if (i == 0)
                {
                    var cash = Ack(await ops.RegistrarCobroAsync(pilot, new(order.OrdenId, 500m, "QA-COBRO-001", Key(), order.Paradas[0].ParadaId)));
                    var cashId = cash.GetProperty("id").GetInt32();
                    var checkedCash = Ack(await ops.ConciliarCobroAsync(manager, cashId, new(500m, "Importe ficticio verificado QA", cash.GetProperty("version").GetString()!, Key())));
                    await ops.RecibirEfectivoAsync(manager, cashId, new(500m, "QA-RECEPCION-EMPRESA-001", checkedCash.GetProperty("version").GetString()!, Key()));
                }
                await core.ConfirmarEntregaParadaAsync(order.OrdenId, order.Paradas[0].ParadaId, order.Paradas[0].CodigoC, "pilot-a2", Key());
                completed.Add(order);
            }
            var zone = Ack(await ops.GuardarZonaAsync(manager, new("Centro ficticio QA", "Santo Domingo", "#168b83",
                [new(18.46, -69.95), new(18.46, -69.88), new(18.52, -69.88), new(18.52, -69.95)], 12, "ParadasPendientes", 80, "07:00", "22:00", true, Key())));
            var zoneId = zone.GetProperty("id").GetInt32();
            await ops.GuardarReglaIncidenciaAsync(manager, new("Sin contacto", 1, 30, "PausarParada", true, false, Key()));
            await ops.GuardarReglaIncidenciaAsync(manager, new("Demorada", 2, 60, "Continuar", true, true, Key()));
            var rule = Ack(await ops.GuardarReglaRemuneracionAsync(manager, new("Servicio fijo QA", "Servicio", 120m, 0m, 10m, 0m, true, Key())));
            await ops.GuardarReglaRemuneracionAsync(manager, new("Parada fija QA", "Parada", 75m, 0m, 0m, 0m, true, Key()));
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(factory.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")).DateTime);
            var settlement = Ack(await ops.GenerarLiquidacionAsync(manager, new(today, today, Key(), PilotoId: factory.PilotA2, ReglaId: rule.GetProperty("id").GetInt32())));
            var settlementId = settlement.GetProperty("id").GetInt32();
            var approved = Ack(await ops.CambiarLiquidacionAsync(manager, settlementId, new("Aprobar", settlement.GetProperty("version").GetString()!, Key())));
            await ops.CambiarLiquidacionAsync(manager, settlementId, new("RegistrarPago", approved.GetProperty("version").GetString()!, Key(), Metodo: "Transferencia externa QA", CuentaUltimos4: "4242", Fecha: today, Referencia: "QA-PAGO-EXTERNO-001"));
            await ops.GenerarLiquidacionAsync(manager, new(today, today, Key(), TiendaId: factory.ShopA));
            var multi = await core.CrearOrdenAsync(factory.TenantA, factory.ShopA, factory.CustomerA, "Una orden, dos entregas QA", operacionId: Key(),
                destinatarios: [new(factory.CustomerA, "Primera parada ficticia"), new(another.Id, "Segunda parada ficticia")]);
            var otherShop = await core.CrearOrdenAsync(factory.TenantA, factory.ShopA2, factory.CustomerA2, "Orden A2 dentro de la misma ruta QA", operacionId: Key());
            var route = Ack(await ops.CrearRutaAsync(manager, new("Ruta multiparada QA · Piloto A2", today,
                [multi.OrdenId, otherShop.OrdenId], factory.PilotA2, zoneId, false, Key())));
            var routeId = route.GetProperty("id").GetInt32();
            await ops.EstadoRutaAsync(manager, routeId, new("Iniciar", route.GetProperty("version").GetString()!, Key()));
            await ops.ReportarIncidenciaAsync(pilot, new("Sin contacto", "Caso ficticio para revisar atención; todavía no se recogió el pedido.", Key(), multi.OrdenId, multi.Paradas[1].ParadaId, routeId));
            var scheduled = await core.CrearOrdenAsync(factory.TenantA, factory.ShopA, factory.CustomerA, "Pendiente programada para mañana QA", operacionId: Key());
            await ops.GuardarDatosEnvioAsync(manager, scheduled.OrdenId, new("QA-PROGRAMADO-001", "Programado", 1, 1.5m, 2, zoneId,
                factory.Clock.GetUtcNow().UtcDateTime.AddDays(1), null, "No disponible para asignar antes de su fecha.", Key()));
            var ready = await core.CrearOrdenAsync(factory.TenantA, factory.ShopA, factory.CustomerA, "Por asignar · libre para QA visual", operacionId: Key());
            var foreign = await core.CrearOrdenAsync(factory.TenantB, factory.ShopB, factory.CustomerB, "Sólo visible en empresa B QA", operacionId: Key());
            manifest = new { entorno = "QA local ficticio; no producción", database = db.Database.GetDbConnection().Database,
                cuentas = new[] { "qa-admin", "manager-a", "manager-b", "shop-a", "qa-shop-a2", "shop-b", "multi", "pilot-a", "pilot-a2", "pilot-b" }
                    .Select(id => new { email = $"{id}@example.test", password = PilotGoFactory.Password }),
                pilotoLibre = new { id = factory.PilotA, cuenta = "pilot-a@example.test", ordenesActivas = 0 },
                rutaId = routeId, zonaId = zoneId, liquidacionPagadaId = settlementId,
                entregadas = completed, ordenMultiparada = multi, ordenRutaA2 = otherShop,
                programada = scheduled, porAsignar = ready, exclusivaEmpresaB = foreign };
        });
        return manifest;
    }
}

// Activación deliberada de Maps JavaScript; nunca geocodifica ni consulta Routes API.
internal sealed class QaBrowserMaps(IGoogleMapsService noMaps, string browserKey) : IGoogleMapsService
{
    public string ApiKey => "";
    public string BrowserApiKey => browserKey;
    public Task<(double Lat, double Lng)?> GeocodificarAsync(string address) => noMaps.GeocodificarAsync(address);
    public Task<double?> DistanciaRutaKmAsync(double a, double b, double c, double d) => noMaps.DistanciaRutaKmAsync(a, b, c, d);
    public double CalcularDistanciaKm(double a, double b, double c, double d) => noMaps.CalcularDistanciaKm(a, b, c, d);
}
