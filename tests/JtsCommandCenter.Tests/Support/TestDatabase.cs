using JtsCommandCenter.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JtsCommandCenter.Tests.Support;

/// <summary>
/// A private in-memory SQLite database built from the real EF model, so data tests run without SQL
/// Server. It lives as long as this object keeps its connection open.
/// </summary>
internal sealed class TestDatabase : IDbContextFactory<CommandCenterDbContext>, IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<CommandCenterDbContext> options;

    public TestDatabase()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<CommandCenterDbContext>().UseSqlite(connection).Options;

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public CommandCenterDbContext CreateDbContext() => new(options);

    public void Dispose() => connection.Dispose();
}
