using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Homelab.Tests;
public sealed class PlanEditingTests
{
    [Fact] public void NullOwnerAndUnspecifiedOrPreEpochDatesAreInvalid()
    {
        var plan = new PlanningItem { Title = "Plan", Owner = null! }; Assert.NotNull(Planning.ValidatePlan(plan));
        plan.Owner = new string('x', 201); Assert.NotNull(Planning.ValidatePlan(plan));
        plan.Owner = ""; plan.DueUtc = new DateTime(2027, 1, 1); Assert.NotNull(Planning.ValidatePlan(plan));
        plan.DueUtc = DateTime.UnixEpoch.AddSeconds(-1); Assert.NotNull(Planning.ValidatePlan(plan));
        plan.DueUtc = DateTime.UtcNow.AddYears(1); Assert.Null(Planning.ValidatePlan(plan));
        plan.DueUtc = null; Assert.Null(Planning.ValidatePlan(plan));
    }
    [Fact] public async Task MigrationPreservesExistingPlanAndHistoryCannotOutliveItsPlan()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        var previousMigration = db.Database.GetMigrations().Single(x => x.EndsWith("_CachedDriveTemperatures"));
        await db.GetService<IMigrator>().MigrateAsync(previousMigration);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Plans (Id, Title, Reason, Status, Currency) VALUES ({id}, 'Keep this evidence', 'Original reason', 'planned', 'USD')");
        await db.Database.MigrateAsync();
        var plan = await db.Plans.SingleAsync();
        Assert.Equal(id, plan.Id); Assert.Equal("", plan.Owner); Assert.Equal(Guid.Empty, plan.Revision);
        Assert.Empty(await db.PlanChanges.ToListAsync());
        db.PlanChanges.Add(new() { PlanId = plan.Id, Actor = "owner", AfterJson = "{}" }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Plans WHERE Id = {plan.Id}"));
        db.ChangeTracker.Clear(); Assert.Equal("Original reason", (await db.Plans.SingleAsync()).Reason);
    }
}
