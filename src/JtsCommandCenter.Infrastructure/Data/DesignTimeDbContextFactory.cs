using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JtsCommandCenter.Infrastructure.Data;

/// <summary>
/// Lets the <c>dotnet ef</c> tools build the context without starting the web app. Commands target the
/// local development database unless given <c>--connection</c>.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CommandCenterDbContext>
{
    public const string LocalConnectionString =
        "Server=.\\SQLEXPRESS;Database=JtsCommandCenter;Trusted_Connection=True;TrustServerCertificate=True";

    public CommandCenterDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CommandCenterDbContext>().UseSqlServer(LocalConnectionString).Options);
}
