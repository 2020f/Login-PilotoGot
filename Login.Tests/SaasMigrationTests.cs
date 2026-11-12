using Login.Application.Services;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class SaasMigrationTests
{
    [Fact]
    public async Task Sql_migration_adds_ledger_without_changing_existing_tenants_or_delivery_state()
    {
        if (Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER") != "1") return;
        using var app = new PilotGoFactory();
        await app.SeedAsync("20261112025055_EnvirapidoRetornosYCajaEmpresa");
        await app.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.OrdenesEntrega.AddRange(
                new OrdenEntrega { ClienteAppId = app.TenantA, TiendaId = app.ShopA, UsuarioFinalId = app.CustomerA,
                    PilotoId = app.PilotA, NumeroOrdenA = 1, CodigoA = "0001", Estado = EstadoOrden.Recolectada, EsAuto = true,
                    AssignedAt = DateTime.UtcNow.AddMinutes(-10), RecolectadaAt = DateTime.UtcNow.AddMinutes(-5),
                    Codigos = new List<CodigoEntrega> { new() { ClienteAppId = app.TenantA, Tipo = TipoCodigo.B_Recoleccion, Codigo = "1111", Usado = true, Reservado = true },
                        new() { ClienteAppId = app.TenantA, Tipo = TipoCodigo.C_Finalizacion, Codigo = "2222", Reservado = true } } },
                new OrdenEntrega { ClienteAppId = app.TenantA, TiendaId = app.ShopA2, UsuarioFinalId = app.CustomerA2,
                    PilotoId = app.PilotA, NumeroOrdenA = 2, CodigoA = "0002", Estado = EstadoOrden.Asignada, EsAuto = false,
                    AssignedAt = DateTime.UtcNow.AddMinutes(-1) },
                new OrdenEntrega { ClienteAppId = app.TenantB, TiendaId = app.ShopB, UsuarioFinalId = app.CustomerB,
                    NumeroOrdenA = 1, CodigoA = "0001", Estado = EstadoOrden.Creada, EsAuto = true });
            await db.SaveChangesAsync();
            var before = await db.ClientesApp.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.PlanId, x.Estado, x.OrdenSeq, x.GestorIdentityUserId }).ToListAsync();
            var shops = await db.Tiendas.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ClienteAppId, x.Activo }).ToListAsync();
            var pilots = await db.Pilotos.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ClienteAppId, x.Activo }).ToListAsync();
            var orders = await db.OrdenesEntrega.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ClienteAppId, x.TiendaId,
                x.PilotoId, x.Estado, x.EsAuto, x.CodigoA, x.NumeroOrdenA, x.AssignedAt, x.RecolectadaAt }).ToListAsync();
            var codes = await db.CodigosEntrega.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ClienteAppId, x.OrdenEntregaId, x.Codigo, x.Tipo, x.Usado, x.Reservado }).ToListAsync();
            await db.GetService<IMigrator>().MigrateAsync("20261112201846_EnvirapidoSaasBilling");
            Assert.Equal(before, await db.ClientesApp.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.PlanId, x.Estado, x.OrdenSeq, x.GestorIdentityUserId }).ToListAsync());
            Assert.Equal(shops, await db.Tiendas.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ClienteAppId, x.Activo }).ToListAsync());
            Assert.Equal(pilots, await db.Pilotos.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ClienteAppId, x.Activo }).ToListAsync());
            Assert.Equal(orders, await db.OrdenesEntrega.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ClienteAppId, x.TiendaId,
                x.PilotoId, x.Estado, x.EsAuto, x.CodigoA, x.NumeroOrdenA, x.AssignedAt, x.RecolectadaAt }).ToListAsync());
            Assert.Equal(codes, await db.CodigosEntrega.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ClienteAppId, x.OrdenEntregaId, x.Codigo, x.Tipo, x.Usado, x.Reservado }).ToListAsync());
            Assert.Empty(await db.SaasInvoices.ToListAsync());
            var billing = new SaasBillingService(db, sp.GetRequiredService<QuotaPolicyService>(), app.Clock);
            var today = billing.Today;
            await billing.IssueInvoiceAsync(app.TenantA, today, today.AddDays(29), today, 100m, "Migración QA", "qa-admin", Guid.NewGuid().ToString("N"));
            Assert.Single(await db.SaasInvoices.ToListAsync());
            Assert.False(await billing.IsExpiredAsync(app.TenantA));
            Assert.False(await billing.IsExpiredAsync(app.TenantB));
        });
    }
}
