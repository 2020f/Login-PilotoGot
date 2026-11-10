using Login.Data;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class CodeMigrationTests
{
    [SqlServerFact]
    public async Task Migration_preserves_existing_codes_and_backfills_active_reservations()
    {
        using var app = new PilotGoFactory();
        await app.SeedAsync("20260908024715_AddPilotoEnLinea");
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            foreach (var (number, state) in new[] { (12345, 2), (12346, 4) })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO OrdenesEntrega (ClienteAppId,TiendaId,UsuarioFinalId,NumeroOrdenA,Estado,EsAuto,CreatedAt)
                    VALUES ({app.TenantA},{app.ShopA},{app.CustomerA},{number},{state},0,GETUTCDATE());");
                var id = await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM OrdenesEntrega WHERE NumeroOrdenA={number}").SingleAsync();
                foreach (var type in new[] { 1, 2 })
                {
                    var code = $"OLD{number}{type}Z";
                    await db.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO CodigosEntrega (OrdenEntregaId,Tipo,Codigo,Usado,CreatedAt)
                        VALUES ({id},{type},{code},0,GETUTCDATE());");
                }
            }
            await db.GetService<IMigrator>().MigrateAsync();
            var orders = await db.OrdenesEntrega.Include(o => o.Codigos).OrderBy(o => o.NumeroOrdenA).ToListAsync();
            Assert.Equal(new[] { 12345, 12346 }, orders.Select(o => o.NumeroOrdenA));
            foreach (var order in orders)
            {
                Assert.Null(order.CodigoA);
                foreach (var code in order.Codigos)
                {
                    Assert.Equal($"OLD{order.NumeroOrdenA}{(int)code.Tipo}Z", code.Codigo);
                    Assert.Equal(app.TenantA, code.ClienteAppId);
                    Assert.Equal(order.Estado != EstadoOrden.Entregada, code.Reservado);
                }
            }
        });
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER") != "1")
            Skip = "Requiere la instancia desechable SQL Server LocalDB (PILOTGO_TEST_SQLSERVER=1).";
    }
}
