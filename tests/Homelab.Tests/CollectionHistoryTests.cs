using System.IO.Compression;
using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Homelab.Tests;

public sealed class CollectionHistoryTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Capabilities = """[{"Name":"pools","Status":"supported","LastSuccessUtc":"2026-01-01T00:00:00Z","ExpectedSeconds":60},{"Name":"smart","Status":"unsupported","ExpectedSeconds":60}]""";
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public HomelabDb Db { get; }
        public Guid Nas { get; } = Guid.NewGuid();
        public Fixture() { Connection.Open(); Db = new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options); }
        public async Task Init() { await Db.Database.MigrateAsync(); Db.Connections.Add(new() { Id = Nas, Enabled = false }); await Db.SaveChangesAsync(); }
        public CollectionRun Row() => new() { ConnectionId = Nas, StartedUtc = Now.Date.AddDays(-100), EndedUtc = Now.Date.AddDays(-100).AddSeconds(2), Result = "partial", CapabilitySummaryJson = Capabilities };
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("not-json")]
    [InlineData("[{\"Name\":\"pools\",\"Status\":null}]")]
    [InlineData("[{\"Name\":\"pools\",\"Status\":\"supported\"},{\"Name\":\"pools\",\"Status\":\"denied\"}]")]
    public void InvalidLegacyCapabilityEvidenceStaysUnknown(string input)
    {
        var result = CollectionHistory.Coverage(input); Assert.False(result.Known); Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SummariesPreserveCountsPeaksAndSeparateOutcomesCapabilitiesCadenceAndNas()
    {
        await using var f = new Fixture(); await f.Init();
        var first = f.Row();
        var slow = f.Row(); slow.EndedUtc = slow.StartedUtc.AddSeconds(20);
        slow.CapabilitySummaryJson = Capabilities.Replace("2026-01-01", "2026-01-02");
        var unknownDuration = f.Row(); unknownDuration.EndedUtc = unknownDuration.StartedUtc.AddSeconds(-1);
        var denied = f.Row(); denied.Result = "denied";
        var capability = f.Row(); capability.CapabilitySummaryJson = Capabilities.Replace("\"Status\":\"supported\"", "\"Status\":\"denied\"");
        var cadence = f.Row(); cadence.CapabilitySummaryJson = Capabilities.Replace(":60", ":120");
        var otherNas = f.Row(); otherNas.ConnectionId = Guid.NewGuid();
        f.Db.Connections.Add(new() { Id = otherNas.ConnectionId, Enabled = false });
        var unknown = f.Row(); unknown.CapabilitySummaryJson = "{}";
        f.Db.CollectionRuns.AddRange(first, slow, unknownDuration, denied, capability, cadence, otherNas, unknown);
        await f.Db.SaveChangesAsync();
        Assert.Equal(8, await CollectionHistory.CompactAsync(f.Db, Now, 90));
        var groups = await f.Db.CollectionDailySummaries.ToListAsync(); Assert.Equal(6, groups.Count);
        var main = Assert.Single(groups, x => x.AttemptCount == 3);
        Assert.Equal(2, main.DurationKnownCount); Assert.Equal(2000, main.DurationMinMs); Assert.Equal(20000, main.DurationMaxMs);
        Assert.DoesNotContain("LastSuccessUtc", main.CapabilitiesJson);
        Assert.False(Assert.Single(groups, x => !x.CapabilitiesKnown).CapabilitiesKnown);
        Assert.Equal(0, await CollectionHistory.CompactAsync(f.Db, Now, 90));
        Assert.Equal(8, await f.Db.CollectionDailySummaries.SumAsync(x => x.AttemptCount));
    }

    [Fact]
    public async Task BatchMergesAndPreservesRecentBoundaryWhileExpiringTwoYearHistory()
    {
        await using var f = new Fixture(); await f.Init();
        f.Db.CollectionRuns.AddRange(Enumerable.Range(0, PoolRetention.BatchSize + 1).Select(_ => f.Row()));
        var recent = f.Row(); recent.StartedUtc = PoolRetention.DetailCutoff(Now, 90); recent.EndedUtc = recent.StartedUtc; f.Db.CollectionRuns.Add(recent);
        await f.Db.SaveChangesAsync();
        Assert.Equal(PoolRetention.BatchSize, await CollectionHistory.CompactAsync(f.Db, Now, 90));
        Assert.Equal(1, await CollectionHistory.CompactAsync(f.Db, Now, 90));
        Assert.Equal(PoolRetention.BatchSize + 1, (await f.Db.CollectionDailySummaries.SingleAsync()).AttemptCount);
        Assert.Equal(recent.Id, (await f.Db.CollectionRuns.SingleAsync()).Id);
        var cutoff = PoolRetention.SummaryCutoff(Now);
        var boundary = f.Row(); boundary.StartedUtc = cutoff; boundary.EndedUtc = cutoff;
        var old = f.Row(); old.StartedUtc = cutoff.AddTicks(-1); old.EndedUtc = old.StartedUtc;
        f.Db.CollectionRuns.AddRange(boundary, old); await f.Db.SaveChangesAsync();
        await CollectionHistory.CompactAsync(f.Db, Now, 90);
        Assert.Equal(1, (await f.Db.CollectionDailySummaries.SingleAsync(x => x.DayUtc == cutoff)).AttemptCount);
        await CollectionHistory.CompactAsync(f.Db, Now.AddDays(1), 90);
        Assert.False(await f.Db.CollectionDailySummaries.AnyAsync(x => x.DayUtc == cutoff));
    }

    [Fact]
    public async Task FailedDeletionRollsBackAggregateAndAudit()
    {
        await using var f = new Fixture(); await f.Init(); f.Db.CollectionRuns.Add(f.Row()); await f.Db.SaveChangesAsync();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_collection BEFORE DELETE ON CollectionRuns BEGIN SELECT RAISE(ABORT, 'failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => CollectionHistory.CompactAsync(f.Db, Now, 90));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, await f.Db.CollectionRuns.CountAsync()); Assert.Equal(0, await f.Db.CollectionDailySummaries.CountAsync()); Assert.Equal(0, await f.Db.Activity.CountAsync());
    }

    [Fact]
    public async Task CollectionOnlyMaintenanceRequiresBackupAndArchivePreservesOriginalLog()
    {
        await using var f = new Fixture(); await f.Init(); f.Db.CollectionRuns.Add(f.Row()); await f.Db.SaveChangesAsync();
        var root = Path.Combine(Path.GetTempPath(), "homelab-collection-retention-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try {
            BackupService Backups(string? path) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Homelab:DataPath"] = Path.Combine(root, "live"), ["Homelab:BackupPath"] = path }).Build());
            Assert.Equal(0, await HistoryRetention.MaintainAsync(f.Db, Backups(null), Now));
            var file = Path.Combine(root, "file"); await File.WriteAllTextAsync(file, "not a directory");
            await Assert.ThrowsAsync<IOException>(() => HistoryRetention.MaintainAsync(f.Db, Backups(file), Now));
            Assert.Equal(1, await f.Db.CollectionRuns.CountAsync()); Assert.Equal(0, await f.Db.CollectionDailySummaries.CountAsync());
            var destination = Path.Combine(root, "backups");
            Assert.Equal(1, await HistoryRetention.MaintainAsync(f.Db, Backups(destination), Now));
            var archive = Assert.Single(Directory.GetFiles(destination, "*.zip")); var restore = Path.Combine(root, "restore"); ZipFile.ExtractToDirectory(archive, restore);
            await using var copy = new SqliteConnection($"Data Source={Path.Combine(restore, "homelab.db")};Pooling=False"); await copy.OpenAsync();
            using var count = copy.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM CollectionRuns"; Assert.Equal(1L, await count.ExecuteScalarAsync());
            Assert.Equal(1, await f.Db.CollectionDailySummaries.CountAsync()); Assert.Equal(0, await f.Db.CollectionRuns.CountAsync());
        } finally { Directory.Delete(root, true); }
    }
}
