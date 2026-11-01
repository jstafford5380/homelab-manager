using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Homelab.Api;

public static class DatabaseLifecycle
{
    public static void Initialize(HomelabDb db, string dataPath)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsLock'";
        var hasTables = Convert.ToInt32(tables.ExecuteScalar()) > 0;
        using var history = connection.CreateCommand();
        history.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='__EFMigrationsHistory'";
        var hasHistory = Convert.ToInt32(history.ExecuteScalar()) > 0;
        if (hasTables && !hasHistory)
            throw new InvalidOperationException("This database predates migrations. Preserve it and run the documented legacy baseline procedure before startup; it will not be overwritten.");
        if (hasTables && db.Database.GetPendingMigrations().Any())
        {
            var backup = Path.Combine(dataPath, "migration-backups"); Directory.CreateDirectory(backup);
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(backup, $"before-migration-{DateTime.UtcNow:yyyyMMddTHHmmssfff}.db") }.ToString());
            target.Open(); connection.BackupDatabase(target);
        }
        db.Database.Migrate();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}

// EF tooling must never start the runtime, collectors, or production database.
public sealed class HomelabDesignFactory : IDesignTimeDbContextFactory<HomelabDb>
{
    public HomelabDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite("Data Source=:memory:").Options);
}
