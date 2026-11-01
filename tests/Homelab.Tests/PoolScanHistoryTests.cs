using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Homelab.Tests;

public sealed class PoolScanHistoryTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private sealed class Fixture : IAsyncDisposable {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public HomelabDb Db { get; }
        public NasConnection Nas { get; } = new();
        public Fixture() { connection.Open(); Db = new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); }
        public async Task Init() { await Db.Database.MigrateAsync(); Db.Connections.Add(Nas); await Db.SaveChangesAsync(); }
        public PoolSnapshot Row(int minute = 0) => new() { ConnectionId=Nas.Id, PoolGuid="pool", ScanFunction="SCRUB",
            ScanState="SCANNING", ScanStartedUtc=Now.AddHours(-1), FetchedUtc=Now.AddMinutes(minute), ScanPercentage=20, ScanErrors=0 };
        public async Task Record(PoolSnapshot row) { await PoolScanHistory.RecordAsync(Db,row); await Db.SaveChangesAsync(); }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    [Fact] public async Task RepeatedPollsUpdateOneScanWithoutInferringCompletion()
    {
        await using var f=new Fixture(); await f.Init(); await f.Record(f.Row());
        var row=f.Row(1); row.ScanState="FINISHED"; row.ScanEndedUtc=Now.AddSeconds(30); row.ScanPercentage=100; row.ScanErrors=null;
        await f.Record(row); await f.Record(row); await f.Record(f.Row(-1));
        var scan=await f.Db.PoolScans.SingleAsync();
        Assert.Equal("FINISHED",scan.ScanState); Assert.Equal(row.ScanEndedUtc,scan.ScanEndedUtc);
        Assert.Null(scan.ScanErrors); Assert.Equal(Now.AddMinutes(-1),scan.FirstFetchedUtc); Assert.Equal(row.FetchedUtc,scan.LastFetchedUtc);
        var noEnd=f.Row(2); noEnd.ScanState="FINISHED"; await f.Record(noEnd);
        Assert.Null(scan.ScanEndedUtc); // FINISHED alone never creates an end timestamp.
    }
    [Theory] [InlineData("missing")] [InlineData("future")] [InlineData("function")] [InlineData("state")] [InlineData("guid")]
    public async Task UnidentifiedOrUnsupportedReportsDoNotInventScans(string invalid)
    {
        await using var f=new Fixture(); await f.Init(); var row=f.Row();
        switch(invalid) {
            case "missing": row.ScanStartedUtc=null; break;
            case "future": row.ScanStartedUtc=Now.AddDays(1); break;
            case "function": row.ScanFunction="UNKNOWN"; break;
            case "state": row.ScanState="NONE"; break;
            case "guid": row.PoolGuid=""; break;
        }
        await f.Record(row); Assert.Empty(await f.Db.PoolScans.ToListAsync());
    }
    [Fact] public async Task GroupsAreScopedByNasPoolFunctionAndStart()
    {
        await using var f=new Fixture(); await f.Init(); await f.Record(f.Row());
        var second=new NasConnection(); f.Db.Connections.Add(second); await f.Db.SaveChangesAsync();
        var row=f.Row(); row.ConnectionId=second.Id; await f.Record(row);
        row=f.Row(); row.PoolGuid="other"; await f.Record(row);
        row=f.Row(); row.ScanFunction="RESILVER"; await f.Record(row);
        row=f.Row(); row.ScanStartedUtc=Now.AddMinutes(-30); await f.Record(row);
        Assert.Equal(5,await f.Db.PoolScans.CountAsync());
    }
    [Fact] public async Task ScanSurvivesDetailedCompactionAndExpiresAfterTwoYears()
    {
        await using var f=new Fixture(); await f.Init();
        var row=f.Row(); row.FetchedUtc=Now.AddDays(-100); row.ScanStartedUtc=row.FetchedUtc.AddHours(-1);
        await f.Record(row); f.Db.PoolSnapshots.Add(row); await f.Db.SaveChangesAsync();
        await PoolRetention.CompactAsync(f.Db,Now,90); Assert.Empty(await f.Db.PoolSnapshots.ToListAsync());
        Assert.Single(await f.Db.PoolScans.ToListAsync());
        Assert.Equal(0,await PoolRetention.CompactAsync(f.Db,row.FetchedUtc.Date.AddYears(2),90));
        await PoolRetention.CompactAsync(f.Db,row.FetchedUtc.Date.AddYears(2).AddDays(1),90);
        Assert.Empty(await f.Db.PoolScans.ToListAsync());
    }
    [Fact] public async Task MigrationBackfillsLatestIdentifiableSourceStateAndPreservesOldSnapshots()
    {
        await using var f=new Fixture(); await f.Init();
        var migrator=f.Db.GetService<IMigrator>();
        var first=f.Row(); first.ScanErrors=7; var last=f.Row(1); last.ScanState="FINISHED"; last.ScanEndedUtc=Now.AddSeconds(30);
        var unknown=f.Row(2); unknown.ScanStartedUtc=null;
        f.Db.PoolSnapshots.AddRange(first,last,unknown); await f.Db.SaveChangesAsync();
        await migrator.MigrateAsync(f.Db.Database.GetMigrations().Single(x=>x.EndsWith("_LocalTemperaturePolicy")));
        await f.Db.Database.MigrateAsync(); f.Db.ChangeTracker.Clear();
        var scan=await f.Db.PoolScans.SingleAsync();
        Assert.Equal("FINISHED",scan.ScanState); Assert.Equal(first.FetchedUtc,scan.FirstFetchedUtc);
        Assert.Equal(last.FetchedUtc,scan.LastFetchedUtc); Assert.Equal(last.ScanEndedUtc,scan.ScanEndedUtc);
        Assert.Equal(7,scan.ScanErrorsPeak); Assert.Equal(0,scan.ScanErrors);
        Assert.Empty(await f.Db.Findings.ToListAsync()); // Migration is evidence backfill, not notification replay.
        Assert.Equal(DateTimeKind.Utc,scan.ScanStartedUtc.Kind);
        Assert.Equal(3,await f.Db.PoolSnapshots.CountAsync());
    }
    [Fact] public async Task ErrorPeakSurvivesZeroMissingAndHistoricalReadWithoutFalseRecovery()
    {
        await using var f=new Fixture(); await f.Init();
        var row=f.Row(); row.ScanErrors=3; await f.Record(row);
        row=f.Row(1); row.ScanErrors=0; row.ScanState="FINISHED"; await f.Record(row);
        row=f.Row(2); row.ScanErrors=null; await f.Record(row);
        var scan=await f.Db.PoolScans.SingleAsync();
        Assert.Null(scan.ScanErrors); Assert.Equal(3,scan.ScanErrorsPeak);
        var finding=await f.Db.Findings.SingleAsync();
        Assert.Equal("urgent",finding.Severity); Assert.Equal("open",finding.State);
        Assert.Contains("do not identify a failed drive",finding.NextStep);
        var evidence=finding.Evidence;
        row=f.Row(-1); row.ScanErrors=8; await f.Record(row);
        Assert.Equal(8,scan.ScanErrorsPeak); Assert.Null(scan.ScanErrors);
        Assert.Equal(Now.AddMinutes(2),scan.LastFetchedUtc); Assert.Equal(evidence,finding.Evidence);
    }
    [Fact] public async Task StableReviewedScanDoesNotReopenUntilHigherCountOrNewScan()
    {
        await using var f=new Fixture(); await f.Init();
        var row=f.Row(); row.ScanErrors=3; await f.Record(row);
        var original=await f.Db.Findings.SingleAsync(); FindingLifecycle.Resolve(original,Now,"Native error details and recovery evidence reviewed."); await f.Db.SaveChangesAsync();
        row=f.Row(1); row.ScanErrors=3; await f.Record(row);
        row=f.Row(2); row.ScanErrors=2; await f.Record(row);
        Assert.Equal(1,await f.Db.Findings.CountAsync()); Assert.Equal("resolved",original.State);
        row=f.Row(3); row.ScanErrors=4; await f.Record(row);
        Assert.Equal(2,await f.Db.Findings.CountAsync());
        row=f.Row(4); row.ScanErrors=1; row.ScanStartedUtc=Now; await f.Record(row);
        Assert.Equal(3,await f.Db.Findings.CountAsync());
    }
    [Theory] [InlineData(-1)] [InlineData(9007199254740992L)]
    public async Task InvalidCountsNeverBecomePeaksOrFindings(long count)
    {
        await using var f=new Fixture(); await f.Init();
        var row=f.Row(); row.ScanErrors=count; await f.Record(row);
        var scan=await f.Db.PoolScans.SingleAsync(); Assert.Null(scan.ScanErrors); Assert.Null(scan.ScanErrorsPeak);
        Assert.Empty(await f.Db.Findings.ToListAsync());
    }
}
