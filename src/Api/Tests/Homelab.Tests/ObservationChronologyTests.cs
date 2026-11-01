using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;

public sealed class ObservationChronologyTests
{
    private static Observation Sample(DateTime time, double value, string source = "original") => new() {
        SourceKey = Guid.NewGuid().ToString(), Metric = "ata.pending_sectors", Unit = "sectors",
        ObservedUtc = time, Value = value, Quality = "observed", Source = source,
        SourceSchema = "fixture-v1", ResetEpoch = "continuous"
    };

    [Theory]
    [InlineData(-15)]
    [InlineData(-10)]
    public async Task BackfilledOtherSourceCannotReopenResolvedEpisode(int offset)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var nas=new NasConnection{Enabled=false};db.Connections.Add(nas);var disk = new Disk{ConnectionId=nas.Id}; db.Disks.Add(disk); await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        async Task Import(Observation item) { await ObservationIngestion.StageAsync(db, disk, item, "owner"); await db.SaveChangesAsync(); }
        await Import(Sample(now.AddMinutes(-30), 4));
        await Import(Sample(now.AddMinutes(-20), 0));
        await Import(Sample(now.AddMinutes(-10), 0));
        var original = await db.Findings.SingleAsync(); Assert.Equal("resolved", original.State);
        var resolved = original.ResolvedUtc;
        await Import(Sample(now.AddMinutes(offset), 9, "older export"));
        Assert.Equal(1, await db.Findings.CountAsync());
        Assert.Equal(resolved, original.ResolvedUtc); Assert.Equal("resolved", original.State);
        Assert.Equal(4, await db.Observations.CountAsync());
        Assert.Contains(await db.Activity.ToListAsync(), x => x.Detail.Contains("historical or same-time"));
        // A genuinely later adverse report must still start a separate episode.
        await Import(Sample(now.AddMinutes(-1), 2, "older export"));
        Assert.Equal(2, await db.Findings.CountAsync());
        Assert.Single(await db.Findings.Where(x => x.State == "open").ToListAsync());
        Assert.Equal("resolved", original.State);
    }

    [Theory]
    [InlineData("unknown", false)]
    [InlineData("unverified", false)]
    [InlineData("observed", true)]
    public async Task OnlyObservedNumericEvidenceOrdersTheLifecycle(string quality, bool suppress)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var nas=new NasConnection{Enabled=false};db.Connections.Add(nas);var disk = new Disk{ConnectionId=nas.Id}; db.Disks.Add(disk); await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        var latest = Sample(now.AddMinutes(-1), 0, "other source"); latest.Quality = quality;
        if (quality == "unknown") { latest.Value = null; latest.MissingReason = "Unavailable"; }
        await ObservationIngestion.StageAsync(db, disk, latest, "owner"); await db.SaveChangesAsync();
        await ObservationIngestion.StageAsync(db, disk, Sample(now.AddMinutes(-5), 4), "owner"); await db.SaveChangesAsync();
        Assert.Equal(suppress ? 0 : 1, await db.Findings.CountAsync());
        Assert.Equal(2, await db.Observations.CountAsync());
    }
}
