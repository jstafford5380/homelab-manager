using System.Text.Json;
using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;

public sealed class ResilverFindingTests
{
    private sealed class Fixture : IAsyncDisposable {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public HomelabDb Db { get; }
        public NasConnection Nas { get; } = new();
        public Disk Disk { get; }
        public Finding Source { get; }
        public PoolSnapshot Snapshot { get; }
        public Fixture() {
            Connection.Open(); Db = new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);
            Disk = new() { ConnectionId = Nas.Id, Serial = "SERIAL", Model = "Model", Present = true };
            Source = new() { ConnectionId = Nas.Id, DiskId = Disk.Id, Rule = "media-present-v1:ata.pending_sectors", Severity = "urgent",
                Evidence = "Pending observation: 8 sectors.", EvidenceObservedUtc = DateTime.UtcNow.AddDays(-20) };
            Snapshot = new() { ConnectionId = Nas.Id, PoolGuid = "pool", Name = "tank", ScanFunction = "RESILVER", ScanState = "SCANNING",
                ScanStartedUtc = DateTime.UtcNow.AddHours(-1), FetchedUtc = DateTime.UtcNow };
            Map();
        }
        public void Map(string status = "inferred", string role = "data", string state = "ONLINE", Guid? diskId = null) {
            var mapping = new VdevDriveMapping(status, diskId ?? Disk.Id, "SERIAL", null, "Model", "Same-cycle inferred mapping.");
            Snapshot.VdevEvidenceJson = JsonSerializer.Serialize(new VdevSnapshotEvidence(VdevEvidence.Schema, "reported", "Fixture", [
                new("member", null, role, "DISK", state, "/dev/sda1", "sda1", "sda", 0, 0, 0, mapping)
            ]), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        public async Task Init() { await Db.Database.MigrateAsync(); Db.Connections.Add(Nas); Db.Disks.Add(Disk); Db.Findings.Add(Source); await Db.SaveChangesAsync(); }
        public async Task Record() { await ResilverFindings.RecordAsync(Db, Snapshot); await Db.SaveChangesAsync(); }
        public Task<List<Finding>> Correlations() => Db.Findings.Where(x => x.Rule.StartsWith(ResilverFindings.RulePrefix)).ToListAsync();
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    [Fact] public async Task CorrelationPreservesOldEvidenceAndDoesNotRefreshOrDuplicateOnPolls() {
        await using var f = new Fixture(); await f.Init(); f.Source.State = "acknowledged"; await f.Db.SaveChangesAsync(); await f.Record();
        var finding = Assert.Single(await f.Correlations()); var evidence = finding.Evidence;
        Assert.Equal(f.Source.EvidenceObservedUtc, finding.EvidenceObservedUtc); Assert.Equal(f.Disk.Id, finding.DiskId);
        Assert.Equal("urgent", finding.Severity); Assert.Contains(f.Source.Id.ToString(), evidence); Assert.Contains("acknowledged", evidence);
        Assert.Contains("may predate", evidence); Assert.Contains("does not verify a physical bay", evidence); Assert.Contains("Do not remove", finding.NextStep);
        finding.State = "acknowledged"; f.Snapshot.FetchedUtc = f.Snapshot.FetchedUtc.AddMinutes(1); await f.Record();
        Assert.Single(await f.Correlations()); Assert.Equal(evidence, finding.Evidence); Assert.Equal("acknowledged", finding.State);
        Assert.Single(await f.Db.Activity.ToListAsync());
    }
    [Theory]
    [InlineData("finished")][InlineData("paused")][InlineData("pause-time")][InlineData("scrub")][InlineData("no-start")][InlineData("future-start")]
    [InlineData("ambiguous")][InlineData("unavailable")][InlineData("cache")][InlineData("spare")][InlineData("offline")]
    [InlineData("resolved")][InlineData("age")][InlineData("endurance")][InlineData("foreign-disk")][InlineData("future-evidence")]
    public async Task UncertainUnrelatedOrInactiveEvidenceDoesNotCreateCorrelation(string scenario) {
        await using var f = new Fixture(); await f.Init();
        switch (scenario) {
            case "finished": f.Snapshot.ScanState = "FINISHED"; break;
            case "paused": f.Snapshot.ScanState = "PAUSED"; break;
            case "pause-time": f.Snapshot.ScanPausedUtc = f.Snapshot.FetchedUtc; break;
            case "scrub": f.Snapshot.ScanFunction = "SCRUB"; break;
            case "no-start": f.Snapshot.ScanStartedUtc = null; break;
            case "future-start": f.Snapshot.ScanStartedUtc = f.Snapshot.FetchedUtc.AddDays(1); break;
            case "ambiguous": case "unavailable": f.Map(status: scenario); break;
            case "cache": case "spare": f.Map(role: scenario); break;
            case "offline": f.Map(state: "OFFLINE"); break;
            case "resolved": f.Source.State = "resolved"; break;
            case "age": f.Source.Rule = "age-review-v1"; break;
            case "endurance": f.Source.Rule = "nvme-endurance-v1"; break;
            case "foreign-disk": f.Map(diskId: Guid.NewGuid()); break;
            case "future-evidence": f.Source.EvidenceObservedUtc = f.Snapshot.FetchedUtc.AddDays(1); break;
        }
        await f.Db.SaveChangesAsync(); await f.Record(); Assert.Empty(await f.Correlations());
    }
    [Fact] public async Task CompletionAndSourceResolutionDoNotEraseCorrelationAndManualReviewSurvivesReplay() {
        await using var f = new Fixture(); await f.Init(); await f.Record(); var finding = Assert.Single(await f.Correlations());
        f.Snapshot.ScanState = "FINISHED"; f.Source.State = "resolved"; await f.Record(); Assert.Equal("open", finding.State);
        FindingLifecycle.Resolve(finding, DateTime.UtcNow, "Reviewed historical context."); f.Source.State = "open"; f.Snapshot.ScanState = "SCANNING"; await f.Record();
        Assert.Single(await f.Correlations()); Assert.Equal("resolved", finding.State);
        f.Snapshot.ScanStartedUtc = f.Snapshot.ScanStartedUtc!.Value.AddMinutes(1); await f.Record();
        Assert.Equal(2, (await f.Correlations()).Count);
    }
    [Fact] public async Task UnknownObservationTimeStaysUnknownAndSpecialVdevCriticalWarningRemainsCritical() {
        await using var f = new Fixture(); await f.Init(); f.Source.EvidenceObservedUtc = null;
        f.Source.Rule = "nvme-warning-v1"; f.Source.Severity = "critical"; f.Map(role: "special"); await f.Db.SaveChangesAsync(); await f.Record();
        var finding = Assert.Single(await f.Correlations()); Assert.Null(finding.EvidenceObservedUtc); Assert.Equal("critical", finding.Severity);
        Assert.Contains("drive observation time unknown", finding.Evidence);
    }
    [Fact] public async Task TransactionRollbackDiscardsCorrelationAndAudit() {
        await using var f = new Fixture(); await f.Init();
        await using (var transaction = await f.Db.Database.BeginTransactionAsync()) { await f.Record(); await transaction.RollbackAsync(); }
        f.Db.ChangeTracker.Clear(); Assert.Empty(await f.Correlations()); Assert.Empty(await f.Db.Activity.ToListAsync());
    }
}
