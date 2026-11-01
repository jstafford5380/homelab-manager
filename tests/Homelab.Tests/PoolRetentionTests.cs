using System.IO.Compression;
using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace Homelab.Tests;

public sealed class PoolRetentionTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 30, 0, DateTimeKind.Utc);
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public HomelabDb Db { get; }
        public NasConnection Nas { get; } = new();
        public Fixture() { Connection.Open(); Db = new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options); }
        public async Task Init() { await Db.Database.MigrateAsync(); Db.Connections.Add(Nas); await Db.SaveChangesAsync(); }
        public PoolSnapshot Row(long? free = 10) => new() { ConnectionId = Nas.Id, PoolGuid = "pool", Status = "ONLINE", FetchedUtc = Now.Date.AddDays(-100), FreeBytes = free };
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Fact]
    public async Task MemberRangesCompactAcrossBatchesAndSeparateChangedAliases()
    {
        await using var f = new Fixture(); await f.Init();
        string Evidence(string errors, string alias = "sda") => VdevEvidence.Capture(System.Text.Json.JsonDocument.Parse("{\"topology\":{\"data\":[{\"guid\":\"member\",\"type\":\"DISK\",\"disk\":\""+alias+"\",\"stats\":{\"read_errors\":"+errors+"}}]}}").RootElement);
        foreach(var value in new[]{"9","null"}) { var row=f.Row();row.VdevEvidenceJson=Evidence(value);f.Db.PoolSnapshots.Add(row); }
        await f.Db.SaveChangesAsync(); await PoolRetention.CompactAsync(f.Db,Now,90);
        var lower=f.Row();lower.VdevEvidenceJson=Evidence("0");
        var changed=f.Row();changed.VdevEvidenceJson=Evidence("1","sdb");
        f.Db.PoolSnapshots.AddRange(lower,changed);await f.Db.SaveChangesAsync();await PoolRetention.CompactAsync(f.Db,Now,90);
        f.Db.ChangeTracker.Clear();
        var rows=await f.Db.PoolDailySummaries.ToListAsync();Assert.Equal(2,rows.Count);
        var summary=Assert.Single(rows,x=>x.SampleCount==3);
        var evidence=System.Text.Json.JsonSerializer.Deserialize<VdevDailySnapshot>(summary.VdevEvidenceJson!,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(3,evidence.Samples);Assert.Equal(new VdevCounterRange(2,0,9),Assert.Single(evidence.Nodes).Read);
        Assert.Empty(await f.Db.PoolSnapshots.ToListAsync());
    }

    [Fact]
    public async Task UnreadableMemberEvidenceDoesNotDeleteDetails()
    {
        await using var f=new Fixture();await f.Init();var row=f.Row();row.VdevEvidenceJson="{}";
        f.Db.PoolSnapshots.Add(row);await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(()=>PoolRetention.CompactAsync(f.Db,Now,90));
        f.Db.ChangeTracker.Clear();Assert.Equal(1,await f.Db.PoolSnapshots.CountAsync());Assert.Empty(await f.Db.PoolDailySummaries.ToListAsync());
    }

    [Fact]
    public async Task PreservesPeaksMissingCountsAndSeparatesStateSchemaAndNas()
    {
        await using var f = new Fixture(); await f.Init();
        var first = f.Row(20); first.ScanErrors = 0; first.AllocatedBytes = 80;
        var second = f.Row(5); second.ScanErrors = 9; second.AllocatedBytes = 95; second.FetchedUtc = second.FetchedUtc.AddHours(2);
        var unknown = f.Row(null);
        var degraded = f.Row(); degraded.Status = "DEGRADED";
        var schema = f.Row(); schema.SourceSchema = "new-schema";
        var scan = f.Row(); scan.ScanState = "SCANNING";
        var otherNas = new NasConnection(); f.Db.Connections.Add(otherNas);
        var other = f.Row(); other.ConnectionId = otherNas.Id;
        f.Db.PoolSnapshots.AddRange(first, second, unknown, degraded, schema, scan, other);
        f.Db.Observations.Add(new() { ConnectionId = f.Nas.Id, SourceKey = "manual", ReceivedUtc = Now.AddYears(-4) });
        f.Db.Findings.Add(new() { ConnectionId = f.Nas.Id, FirstSeenUtc = Now.AddYears(-4) });
        await f.Db.SaveChangesAsync();
        Assert.Equal(7, await PoolRetention.CompactAsync(f.Db, Now, 90));
        var summaries = await f.Db.PoolDailySummaries.ToListAsync();
        Assert.Equal(5, summaries.Count);
        var summary = Assert.Single(summaries, x => x.SampleCount == 3);
        Assert.Equal(2, summary.FreeBytesKnownCount); Assert.Equal(5, summary.FreeBytesMin); Assert.Equal(20, summary.FreeBytesMax);
        Assert.Equal(9, summary.ScanErrorsMax); Assert.Equal(2, summary.ScanErrorsKnownCount);
        Assert.Equal(80, summary.AllocatedBytesMin); Assert.Equal(95, summary.AllocatedBytesMax);
        Assert.Null(summary.SizeBytesMin); Assert.Equal(0, summary.SizeBytesKnownCount);
        Assert.Equal(first.FetchedUtc, summary.FirstFetchedUtc); Assert.Equal(second.FetchedUtc, summary.LastFetchedUtc);
        Assert.Equal(1, await f.Db.Observations.CountAsync()); Assert.Equal(1, await f.Db.Findings.CountAsync());
    }

    [Fact]
    public async Task IntegrityCodesRemainSeparateAcrossBatchesAndUnknownDoesNotBecomeOk()
    {
        await using var f = new Fixture(); await f.Init();
        foreach (var code in new string?[] { null, "OK", "CORRUPT_DATA", "CORRUPT_POOL", "FUTURE_CODE" }) {
            var row = f.Row(); row.StatusCode = code; f.Db.PoolSnapshots.Add(row);
        }
        await f.Db.SaveChangesAsync();
        Assert.Equal(5, await PoolRetention.CompactAsync(f.Db, Now, 90));
        foreach (var code in new string?[] { null, "OK", "CORRUPT_DATA", "CORRUPT_POOL", "FUTURE_CODE" }) {
            var row = f.Row(); row.StatusCode = code; row.ScanErrors = 7; f.Db.PoolSnapshots.Add(row);
        }
        await f.Db.SaveChangesAsync();
        Assert.Equal(5, await PoolRetention.CompactAsync(f.Db, Now, 90));
        Assert.Equal(0, await PoolRetention.CompactAsync(f.Db, Now, 90));
        f.Db.ChangeTracker.Clear();
        var summaries = await f.Db.PoolDailySummaries.ToListAsync();
        Assert.Equal(5, summaries.Count);
        Assert.All(summaries, x => { Assert.Equal(2, x.SampleCount); Assert.Equal(7, x.ScanErrorsMax); Assert.Equal(1, x.ScanErrorsKnownCount); });
        Assert.Single(summaries, x => x.StatusCode is null);
        Assert.Single(summaries, x => x.StatusCode == "CORRUPT_DATA");
    }

    [Fact]
    public async Task LegacyUnknownCodeGroupContinuesWithoutDuplicateOrRelabelling()
    {
        await using var f = new Fixture(); await f.Init();
        var row = f.Row(); row.SourceSchema = "truenas-24.10-pool-v1";
        var legacyJson = System.Text.Json.JsonSerializer.Serialize(new { row.ConnectionId, row.PoolGuid,
            Day = row.FetchedUtc.Date, row.SourceSchema, row.Status, row.ScanFunction, row.ScanState });
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(legacyJson)));
        f.Db.PoolDailySummaries.Add(new() { GroupKey = key, ConnectionId = row.ConnectionId,
            PoolGuid = row.PoolGuid, DayUtc = row.FetchedUtc.Date, SourceSchema = row.SourceSchema,
            Status = row.Status, FirstFetchedUtc = row.FetchedUtc, LastFetchedUtc = row.FetchedUtc, SampleCount = 3 });
        f.Db.PoolSnapshots.Add(row); await f.Db.SaveChangesAsync();
        await PoolRetention.CompactAsync(f.Db, Now, 90);
        var summary = await f.Db.PoolDailySummaries.SingleAsync();
        Assert.Equal(key, summary.GroupKey); Assert.Equal(4, summary.SampleCount); Assert.Null(summary.StatusCode);
    }

    [Fact]
    public async Task AdditiveMigrationLeavesExistingDailyEvidenceUnknown()
    {
        await using var f = new Fixture(); await f.Init();
        f.Db.PoolSnapshots.Add(f.Row()); await f.Db.SaveChangesAsync();
        await PoolRetention.CompactAsync(f.Db, Now, 90);
        var before = await f.Db.PoolDailySummaries.SingleAsync();
        var key = before.GroupKey;
        var migrator = f.Db.GetService<IMigrator>();
        var previous = f.Db.Database.GetMigrations().Single(x => x.EndsWith("_PoolStatusCode"));
        await migrator.MigrateAsync(previous);
        await f.Db.Database.MigrateAsync();
        f.Db.ChangeTracker.Clear();
        var after = await f.Db.PoolDailySummaries.SingleAsync();
        Assert.Equal(key, after.GroupKey); Assert.Equal(1, after.SampleCount);
        Assert.Null(after.StatusCode); Assert.Equal(10, after.FreeBytesMin);
    }

    [Fact]
    public async Task BatchRetryMergesWithoutDoubleCountingAndRetainsRecentBoundary()
    {
        await using var f = new Fixture(); await f.Init();
        f.Db.PoolSnapshots.AddRange(Enumerable.Range(0, PoolRetention.BatchSize + 1).Select(i => f.Row(i)));
        var boundary = f.Row(); boundary.FetchedUtc = PoolRetention.DetailCutoff(Now, 90); f.Db.PoolSnapshots.Add(boundary);
        await f.Db.SaveChangesAsync();
        Assert.Equal(PoolRetention.BatchSize, await PoolRetention.CompactAsync(f.Db, Now, 90));
        Assert.Equal(1, await PoolRetention.CompactAsync(f.Db, Now, 90));
        Assert.Equal(0, await PoolRetention.CompactAsync(f.Db, Now, 90));
        var summary = await f.Db.PoolDailySummaries.SingleAsync();
        Assert.Equal(PoolRetention.BatchSize + 1, summary.SampleCount);
        Assert.Equal(PoolRetention.BatchSize, summary.FreeBytesMax);
        Assert.Equal(boundary.Id, (await f.Db.PoolSnapshots.SingleAsync()).Id);
    }

    [Fact]
    public async Task FailedDeleteRollsBackSummaryAndAuditTogether()
    {
        await using var f = new Fixture(); await f.Init(); f.Db.PoolSnapshots.Add(f.Row()); await f.Db.SaveChangesAsync();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_retention BEFORE DELETE ON PoolSnapshots BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => PoolRetention.CompactAsync(f.Db, Now, 90));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, await f.Db.PoolSnapshots.CountAsync()); Assert.Equal(0, await f.Db.PoolDailySummaries.CountAsync());
        Assert.Equal(0, await f.Db.Activity.CountAsync());
        await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_retention;");
        await PoolRetention.CompactAsync(f.Db, Now, 90);
        Assert.Equal(1, (await f.Db.PoolDailySummaries.SingleAsync()).SampleCount);
    }

    [Fact]
    public async Task TwoYearBoundaryIsUtcAndExpiredGroupsAreRemoved()
    {
        await using var f = new Fixture(); await f.Init();
        var boundary = f.Row(); boundary.FetchedUtc = Now.Date.AddYears(-2);
        var expired = f.Row(); expired.FetchedUtc = boundary.FetchedUtc.AddTicks(-1);
        f.Db.PoolSnapshots.AddRange(boundary, expired); await f.Db.SaveChangesAsync();
        await PoolRetention.CompactAsync(f.Db, Now, 90);
        Assert.Equal(1, (await f.Db.PoolDailySummaries.SingleAsync()).SampleCount);
        await PoolRetention.CompactAsync(f.Db, Now.AddDays(1), 90);
        Assert.Empty(await f.Db.PoolDailySummaries.ToListAsync());
    }

    [Fact]
    public async Task MissingOrFailedBackupPreservesDetailsAndSuccessfulBackupContainsOriginals()
    {
        await using var f = new Fixture(); await f.Init(); f.Db.PoolSnapshots.Add(f.Row()); await f.Db.SaveChangesAsync();
        var root = Path.Combine(Path.GetTempPath(), "homelab-retention-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try {
            BackupService Service(string? destination) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["Homelab:DataPath"] = Path.Combine(root, "data"), ["Homelab:BackupPath"] = destination
            }).Build());
            Assert.Equal(0, await HistoryRetention.MaintainAsync(f.Db, Service(null), Now));
            var blocked = Path.Combine(root, "file"); await File.WriteAllTextAsync(blocked, "not a directory");
            await Assert.ThrowsAsync<IOException>(() => HistoryRetention.MaintainAsync(f.Db, Service(blocked), Now));
            Assert.Equal(1, await f.Db.PoolSnapshots.CountAsync()); Assert.Equal(0, await f.Db.PoolDailySummaries.CountAsync());
            var backups = Path.Combine(root, "backups");
            Assert.Equal(1, await HistoryRetention.MaintainAsync(f.Db, Service(backups), Now));
            var archive = Assert.Single(Directory.GetFiles(backups, "*.zip"));
            var restored = Path.Combine(root, "restore"); ZipFile.ExtractToDirectory(archive, restored);
            await using var copy = new SqliteConnection($"Data Source={Path.Combine(restored, "homelab.db")};Pooling=False"); await copy.OpenAsync();
            using var count = copy.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM PoolSnapshots";
            Assert.Equal(1L, await count.ExecuteScalarAsync());
            Assert.Equal(0, await f.Db.PoolSnapshots.CountAsync()); Assert.Equal(1, await f.Db.PoolDailySummaries.CountAsync());
        } finally { Directory.Delete(root, true); }
    }
}
