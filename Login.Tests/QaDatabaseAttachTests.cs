using Login.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Login.Tests;

public sealed class QaDatabaseAttachTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("PilotGoTest_not-a-guid")]
    [InlineData("PilotGoTest_00000000000000000000000000000000;Database=master")]
    public void Attach_rejects_names_outside_the_disposable_QA_namespace(string name)
        => Assert.Throws<ArgumentException>(() => new PilotGoFactory(name));

    [SqlServerFact]
    public async Task Attached_factory_preserves_rows_and_owner_factory_still_cleans_its_database()
    {
        using var owner = new PilotGoFactory();
        await owner.SeedAsync();
        string connectionString = "", database = "";
        await owner.InScopeAsync(sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            connectionString = db.Database.GetConnectionString()!;
            database = db.Database.GetDbConnection().Database;
            return Task.CompletedTask;
        });
        using (var attached = new PilotGoFactory(database))
        {
            Assert.True(attached.PreservesExistingSqlDatabase);
            await attached.LoadExistingQaAsync();
            Assert.Equal(owner.TenantA, attached.TenantA);
            Assert.Equal(owner.TenantB, attached.TenantB);
            Assert.Equal(owner.ShopA2, attached.ShopA2);
            Assert.Equal(owner.PilotA, attached.PilotA);
            await Assert.ThrowsAsync<InvalidOperationException>(() => attached.SeedAsync());
        }
        await using (var retained = new SqlConnection(connectionString))
        {
            await retained.OpenAsync();
            await using var count = retained.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM AspNetUsers;";
            Assert.Equal(7, Convert.ToInt32(await count.ExecuteScalarAsync()));
        }
        owner.Dispose();
        var masterSettings = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var master = new SqlConnection(masterSettings.ConnectionString);
        await master.OpenAsync();
        await using var existence = master.CreateCommand();
        existence.CommandText = "SELECT DB_ID(@name);";
        existence.Parameters.AddWithValue("@name", database);
        Assert.Equal(DBNull.Value, await existence.ExecuteScalarAsync());
    }

    [SqlServerFact]
    public async Task Attaching_a_missing_QA_database_never_creates_it()
    {
        var database = $"PilotGoTest_{Guid.NewGuid():N}";
        using var attached = new PilotGoFactory(database);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attached.LoadExistingQaAsync());
        await attached.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Database.CanConnectAsync());
        });
    }
}
