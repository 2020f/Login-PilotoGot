using System.Data;
using System.Globalization;
using System.Text.Json;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class ManagedMigrationTests
{
    private const string Previous = "20261112201846_EnvirapidoSaasBilling";
    private const string Current = "20261117230916_EnvirapidoReasignacionPorParadas";
    private static string Key() => Guid.NewGuid().ToString("N");

    [SqlServerFact]
    public async Task Upgrade_preserves_legacy_orders_codes_terminal_stops_and_visit_identity_while_repairing_member_ids()
    {
        using var app = new PilotGoFactory();
        await app.SeedAsync(Previous);
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var at = new DateTime(2026, 11, 16, 14, 30, 0, DateTimeKind.Utc);
            var first = await InsertLegacyOrder(db, app, 8110, false, at);
            var terminal = await InsertLegacyOrder(db, app, 8220, true, at);
            var version = Key();
            await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO RutasOperativas
                (ClienteAppId,Nombre,Fecha,PilotoId,Estado,PermitirReordenarPiloto,IniciadaAt,Version,CreatedAt,UpdatedAt)
                VALUES ({app.TenantA},{"Ruta histórica QA"},{at.Date},{app.PilotA},{(int)EstadoRutaOperativa.Activa},0,{at},{version},{at},{at});");
            var routeId = await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM RutasOperativas WHERE Version={version}").SingleAsync();
            var position = 1;
            foreach (var source in new[] { first, terminal })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO RutaOrdenesOperativas (ClienteAppId,RutaId,OrdenEntregaId,Vigente)
                    VALUES ({app.TenantA},{routeId},{source.OrderId},1);");
                // The old schema referenced (tenant,route,order), not this scalar.
                // Reproduce historical rows whose scalar RutaOrdenId needs repair.
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO PuntosRutaOperativos
                    (ClienteAppId,RutaId,OrdenEntregaId,RutaOrdenId,ParadaEntregaId,Tipo,Posicion,LlegadaAt)
                    VALUES ({app.TenantA},{routeId},{source.OrderId},987654,NULL,1,{position++},{at});");
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO PuntosRutaOperativos
                    (ClienteAppId,RutaId,OrdenEntregaId,RutaOrdenId,ParadaEntregaId,Tipo,Posicion,LlegadaAt)
                    VALUES ({app.TenantA},{routeId},{source.OrderId},987654,{source.StopId},2,{position++},{at});");
            }
            const string orderSql = "SELECT Id,ClienteAppId,TiendaId,UsuarioFinalId,PilotoId,NumeroOrdenA,CodigoA,Estado,EsAuto,CreatedAt,AssignedAt,RecolectadaAt,EntregadaAt,FinalizadaAt,RecogidaNombre,DestinoDireccion FROM OrdenesEntrega ORDER BY Id";
            const string stopSql = "SELECT Id,ClienteAppId,OrdenEntregaId,Posicion,Nombre,Direccion,Estado,EntregadaAt,DevueltaAt FROM ParadasEntrega ORDER BY Id";
            const string codeSql = "SELECT Id,ClienteAppId,OrdenEntregaId,ParadaEntregaId,Tipo,Codigo,Usado,UsadoAt,Reservado,CreatedAt FROM CodigosEntrega ORDER BY Id";
            const string routeSql = "SELECT Id,ClienteAppId,Nombre,Fecha,PilotoId,Estado,IniciadaAt,FinalizadaAt,Version FROM RutasOperativas ORDER BY Id";
            const string visitSql = "SELECT Id,ClienteAppId,RutaId,OrdenEntregaId,ParadaEntregaId,Tipo,Posicion,LlegadaAt FROM PuntosRutaOperativos ORDER BY Id";
            var before = new[] { await Snapshot(db, orderSql), await Snapshot(db, stopSql), await Snapshot(db, codeSql), await Snapshot(db, routeSql), await Snapshot(db, visitSql) };
            await db.GetService<IMigrator>().MigrateAsync(Current);
            db.ChangeTracker.Clear();
            Assert.Equal(before, new[] { await Snapshot(db, orderSql), await Snapshot(db, stopSql), await Snapshot(db, codeSql), await Snapshot(db, routeSql), await Snapshot(db, visitSql) });
            var orders = await db.OrdenesEntrega.AsNoTracking().Include(o => o.Paradas).OrderBy(o => o.Id).ToListAsync();
            Assert.All(orders, o => Assert.False(o.GestionPorParadas));
            var delivered = orders.Single(o => o.Id == terminal.OrderId);
            Assert.Equal(EstadoOrden.Entregada, delivered.Estado); Assert.NotNull(delivered.FinalizadaAt);
            Assert.Equal(EstadoParadaEntrega.Entregada, delivered.Paradas.Single().Estado);
            var memberships = await db.RutaOrdenesOperativas.AsNoTracking().ToListAsync();
            Assert.All(memberships, m => { Assert.Null(m.ParadaEntregaId); Assert.True(m.Vigente); });
            var visits = await db.PuntosRutaOperativos.AsNoTracking().ToListAsync();
            Assert.All(visits, p => Assert.Equal(memberships.Single(m => m.ClienteAppId == p.ClienteAppId && m.RutaId == p.RutaId && m.OrdenEntregaId == p.OrdenEntregaId).Id, p.RutaOrdenId));
            Assert.Empty(await db.RetirosParadas.AsNoTracking().ToListAsync());
            await db.GetService<IMigrator>().MigrateAsync(Current);
            Assert.Equal(before[2], await Snapshot(db, codeSql));
            Assert.Single((await db.Database.GetAppliedMigrationsAsync()).Where(name => name == Current));
        });
    }

    [SqlServerFact]
    public async Task Downgrade_refuses_both_managed_orders_and_preserved_withdrawal_history_without_removing_data()
    {
        foreach (var managedFlag in new[] { true, false })
        {
            using var app = new PilotGoFactory();
            await app.SeedAsync(Current);
            await app.InScopeAsync(async sp =>
            {
                var created = await sp.GetRequiredService<IOrdenService>().CrearOrdenAsync(app.TenantA, app.ShopA, app.CustomerA, operacionId: Key());
                var db = sp.GetRequiredService<ApplicationDbContext>();
                var order = await db.OrdenesEntrega.Include(o => o.Paradas).SingleAsync(o => o.Id == created.OrdenId);
                order.GestionPorParadas = managedFlag;
                if (!managedFlag)
                    db.RetirosParadas.Add(new RetiroParadaOperativa
                    {
                        ClienteAppId = app.TenantA, OrdenEntregaId = order.Id, ParadaEntregaId = order.Paradas.Single().Id,
                        Estado = "Rechazada", Motivo = "Historial QA que debe conservarse", SolicitadaPor = "manager-a", Abierta = false
                    });
                await db.SaveChangesAsync();
                var codeIds = await db.CodigosEntrega.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
                Assert.Contains("No se puede revertir el esquema con reasignaciones", failure.GetBaseException().Message);
                Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync());
                db.ChangeTracker.Clear();
                Assert.Equal(managedFlag, (await db.OrdenesEntrega.AsNoTracking().SingleAsync()).GestionPorParadas);
                Assert.Equal(codeIds, await db.CodigosEntrega.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).ToListAsync());
                Assert.Equal(managedFlag ? 0 : 1, await db.RetirosParadas.CountAsync());
                Assert.Single(await db.ParadasEntrega.AsNoTracking().ToListAsync());
            });
        }
    }

    private static async Task<(int OrderId, int StopId)> InsertLegacyOrder(ApplicationDbContext db, PilotGoFactory app, int number, bool terminal, DateTime at)
    {
        var state = terminal ? EstadoOrden.Entregada : EstadoOrden.Recolectada;
        var stopState = terminal ? EstadoParadaEntrega.Entregada : EstadoParadaEntrega.Pendiente;
        DateTime? delivered = terminal ? at.AddMinutes(10) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO OrdenesEntrega
            (ClienteAppId,TiendaId,UsuarioFinalId,PilotoId,NumeroOrdenA,CodigoA,Estado,EsAuto,CreatedAt,AssignedAt,RecolectadaAt,EntregadaAt,FinalizadaAt,
                DatosEntregaCongelados,RecogidaNombre,RecogidaDireccion,RecogidaTelefono,DestinoNombre,DestinoDireccion,DestinoTelefono)
            VALUES ({app.TenantA},{app.ShopA},{app.CustomerA},{app.PilotA},{number},{number.ToString("D4")},{(int)state},0,{at},{at},{at},{delivered},{delivered},
                1,{"Origen histórico"},{"Calle histórica"},{"5550000"},{"Destino conservado"},{"Dirección histórica"},{"5550001"});");
        var id = await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM OrdenesEntrega WHERE NumeroOrdenA={number} AND ClienteAppId={app.TenantA}").SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ParadasEntrega
            (ClienteAppId,OrdenEntregaId,UsuarioFinalId,TiendaId,Posicion,Nombre,Direccion,Telefono,Estado,EntregadaAt,CreatedAt)
            VALUES ({app.TenantA},{id},{app.CustomerA},{app.ShopA},1,{"Destino conservado"},{"Dirección histórica"},{"5550001"},{(int)stopState},{delivered},{at});");
        var stopId = await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM ParadasEntrega WHERE OrdenEntregaId={id}").SingleAsync();
        foreach (var type in new[] { TipoCodigo.B_Recoleccion, TipoCodigo.C_Finalizacion })
        {
            var isPickup = type == TipoCodigo.B_Recoleccion; var used = isPickup || terminal;
            DateTime? usedAt = used ? at : null; int? codeStop = isPickup ? null : stopId;
            await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO CodigosEntrega
                (ClienteAppId,OrdenEntregaId,ParadaEntregaId,Tipo,Codigo,Usado,UsadoAt,Reservado,CreatedAt)
                VALUES ({app.TenantA},{id},{codeStop},{(int)type},{(number + (isPickup ? 1 : 2)).ToString("D4")},{used},{usedAt},{!terminal},{at});");
        }
        return (id, stopId);
    }

    private static async Task<string> Snapshot(ApplicationDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection(); var closed = connection.State == ConnectionState.Closed;
        if (closed) await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string?[]>();
            while (await reader.ReadAsync())
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i) is DateTime date
                    ? date.ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                rows.Add(row);
            }
            return JsonSerializer.Serialize(rows);
        }
        finally { if (closed) await connection.CloseAsync(); }
    }
}
