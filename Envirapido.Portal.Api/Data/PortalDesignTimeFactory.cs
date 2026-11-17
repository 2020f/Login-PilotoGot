using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Envirapido.Portal.Api.Data;

// EF se ejecuta explícitamente contra el portal. No toma DefaultConnection,
// no arranca workers y no obtiene secretos de autenticación para generar SQL.
public sealed class PortalDesignTimeFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__PortalConnection");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("EF requiere ConnectionStrings__PortalConnection explícita para la base del portal.");
        var sql = new SqlConnectionStringBuilder(connection);
        if (!sql.InitialCatalog.StartsWith("EnvirapidoPortal", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("EF no puede operar sobre una base ajena a EnvirapidoPortal.");
        return new(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(connection).Options);
    }
}
