using System.Text.RegularExpressions;
using Envirapido.Portal.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Envirapido.Portal.Tests;

public sealed class CoreMigrationTests
{
    [PortalMigrationSqlFact]
    public async Task Initial_migration_and_idempotent_script_preserve_rows_on_reexecution_in_a_new_SQL_database()
    {
        await WithNewDatabase(async (db, connection) =>
        {
            await db.Database.MigrateAsync();
            Assert.Contains("20261117144550_PortalInitial", await db.Database.GetAppliedMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            var user = new PortalUser { Email = "migration-only@example.test", NormalizedEmail = "migration-only@example.test", PasswordHash = "No-Login-Fixture", CreatedAt = DateTimeOffset.UtcNow, TermsVersion = "QA", PrivacyVersion = "QA" };
            db.Users.Add(user); await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
            var script = await File.ReadAllTextAsync(ScriptPath());
            await ApplyScript(connection, script); await ApplyScript(connection, script);
            Assert.Equal(user.Id, (await db.Users.AsNoTracking().SingleAsync()).Id);
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        });
    }

    [PortalMigrationSqlFact]
    public async Task Manual_script_rejects_a_QA_database_with_CRM_shaped_tables_before_any_portal_DDL()
    {
        await WithNewDatabase(async (db, connection) =>
        {
            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync(); await using var create = sql.CreateCommand();
                create.CommandText = "CREATE TABLE dbo.AspNetUsers (Id nvarchar(450) NOT NULL PRIMARY KEY);"; await create.ExecuteNonQueryAsync();
            }
            var failure = await Assert.ThrowsAsync<SqlException>(() => ApplyScript(connection, File.ReadAllText(ScriptPath())));
            Assert.Equal(51001, failure.Number);
            await using var check = new SqlConnection(connection); await check.OpenAsync();
            await using var command = check.CreateCommand(); command.CommandText = "SELECT OBJECT_ID(N'dbo.Users',N'U');";
            Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
        });
    }

    [PortalMigrationSqlFact]
    public async Task Manual_script_creates_no_portal_tables_when_executor_continues_after_guard_failures()
    {
        foreach (var portalDatabaseName in new[] { true, false })
        {
            await WithNewDatabase(async (_, connection) =>
            {
                await using var sql = new SqlConnection(connection); await sql.OpenAsync();
                await using (var fixture = sql.CreateCommand())
                {
                    fixture.CommandText = "CREATE TABLE dbo.AspNetUsers (Id nvarchar(450) NOT NULL PRIMARY KEY); INSERT INTO dbo.AspNetUsers (Id) VALUES (N'preserve-original-qa-row');";
                    await fixture.ExecuteNonQueryAsync();
                }
                var failures = new List<int>();
                foreach (var batch in ScriptBatches(File.ReadAllText(ScriptPath())))
                {
                    await using var command = sql.CreateCommand(); command.CommandText = batch; command.CommandTimeout = 120;
                    // Deliberately emulate an unsafe executor that submits every GO batch.
                    try { await command.ExecuteNonQueryAsync(); }
                    catch (SqlException error) { failures.Add(error.Number); }
                }
                Assert.NotEmpty(failures);
                Assert.Equal(portalDatabaseName ? 51001 : 51000, failures[0]);
                Assert.All(failures, number => Assert.Contains(number, new[] { 51000, 51001 }));
                await using (var tableCount = sql.CreateCommand())
                {
                    tableCount.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0;";
                    Assert.Equal(1, (int)(await tableCount.ExecuteScalarAsync())!);
                }
                await using (var original = sql.CreateCommand())
                {
                    original.CommandText = "SELECT Id FROM dbo.AspNetUsers;";
                    Assert.Equal("preserve-original-qa-row", await original.ExecuteScalarAsync());
                }
                await using var history = sql.CreateCommand(); history.CommandText = "SELECT OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U');";
                Assert.Equal(DBNull.Value, await history.ExecuteScalarAsync());
            }, portalDatabaseName);
        }
    }

    private static string ScriptPath()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "portal", "sql", "PortalInitial.idempotent.sql"));
        if (!File.Exists(path)) throw new InvalidOperationException("Falta el script SQL versionado del portal.");
        return path;
    }
    private static async Task ApplyScript(string connection, string script)
    {
        await using var sql = new SqlConnection(connection); await sql.OpenAsync();
        foreach (var batch in ScriptBatches(script))
        {
            await using var command = sql.CreateCommand(); command.CommandText = batch; command.CommandTimeout = 120; await command.ExecuteNonQueryAsync();
        }
    }
    private static IEnumerable<string> ScriptBatches(string script) =>
        Regex.Split(script, "^\\s*GO\\s*(?:--[^\\r\\n]*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(batch => !string.IsNullOrWhiteSpace(batch));
    private static async Task WithNewDatabase(Func<PortalDbContext, string, Task> action, bool portalDatabaseName = true)
    {
        var instance = Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER_INSTANCE") ?? "PilotGoVisualQA_20261111";
        if (!Regex.IsMatch(instance, "^[A-Za-z0-9_-]{1,64}$")) throw new InvalidOperationException("Instancia QA inválida.");
        var name = (portalDatabaseName ? "EnvirapidoPortalTest_" : "PortalMigrationGuardTest_") + Guid.NewGuid().ToString("N");
        if (!Regex.IsMatch(name, "^(EnvirapidoPortalTest_|PortalMigrationGuardTest_)[a-f0-9]{32}$")) throw new InvalidOperationException("Nombre QA inválido.");
        var settings = new SqlConnectionStringBuilder { DataSource = "(localdb)\\" + instance, InitialCatalog = name, IntegratedSecurity = true, TrustServerCertificate = true };
        var connection = settings.ConnectionString;
        var masterSettings = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        var owned = false;
        await using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(connection).Options);
        try
        {
            await using (var master = new SqlConnection(masterSettings.ConnectionString))
            {
                await master.OpenAsync(); await using var existing = master.CreateCommand(); existing.CommandText = "SELECT DB_ID(@name);"; existing.Parameters.AddWithValue("@name", name);
                Assert.Equal(DBNull.Value, await existing.ExecuteScalarAsync());
                await using var create = master.CreateCommand(); create.CommandText = "CREATE DATABASE [" + name + "];"; await create.ExecuteNonQueryAsync(); owned = true;
            }
            await action(db, connection);
        }
        finally
        {
            if (owned)
            {
                if (db.Database.GetDbConnection().Database != name) throw new InvalidOperationException("No se eliminará una base ajena a este test.");
                await db.Database.EnsureDeletedAsync();
            }
        }
    }
}
public sealed class PortalMigrationSqlFactAttribute : FactAttribute
{
    public PortalMigrationSqlFactAttribute()
    { if (Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER") != "1") Skip = "Requiere SQL Server QA explícito; no se ejecuta contra SQLite."; }
}
