using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Tests;
public sealed class UtcPersistenceTests
{
    [Fact] public async Task ExistingUtcValuesKeepTheirClockTimeAndLocalInputsRoundTripAsUtc()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        // Simulate the existing SQLite representation, which has no timezone marker.
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Activity (AtUtc, Actor, Action, Detail) VALUES ('2026-10-31 20:15:00', 'legacy-owner', 'Legacy event', '')");
        var instant = new DateTime(2026, 10, 31, 20, 15, 0, DateTimeKind.Utc);
        db.Activity.Add(new() { AtUtc = instant.ToLocalTime(), Action = "Local-kind input" }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var events = await db.Activity.Where(e => e.AtUtc >= instant).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => { Assert.Equal(instant, e.AtUtc); Assert.Equal(DateTimeKind.Utc, e.AtUtc.Kind); });
        var oldEvent = events.Single(e => e.Action == "Legacy event");
        Assert.Equal(20, oldEvent.AtUtc.Hour); // No reinterpretation using the host's timezone.
    }
}
