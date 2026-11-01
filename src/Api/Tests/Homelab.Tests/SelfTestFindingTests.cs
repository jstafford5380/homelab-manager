using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
namespace Homelab.Tests;
public sealed class SelfTestFindingTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static SmartReportPreview Preview(int minute=0, int hours=100, int status=112, bool verified=true) {
        var root = new JsonObject {
            ["json_format_version"] = new JsonArray(1,0),["smartctl"] = new JsonObject { ["version"] = new JsonArray(7,4),["exit_status"] = verified ? 0 : 4 },
            ["device"] = new JsonObject { ["protocol"] = "ATA" },["serial_number"] = "A",["model_name"] = "model",
            ["local_time"] = new JsonObject { ["time_t"] = new DateTimeOffset(Now.AddMinutes(minute)).ToUnixTimeSeconds() },
            ["ata_smart_self_test_log"] = new JsonObject { ["standard"] = new JsonObject { ["table"] = new JsonArray(new JsonObject {
                ["type"] = new JsonObject { ["value"] = 2 },["status"] = new JsonObject { ["value"] = status },["lifetime_hours"] = hours,["lba"] = 42
            }) } }
        };
        return SmartReports.Parse(new Disk { Serial="A",Model="model" },new(root.ToJsonString()),Now.AddHours(1));
    }
    private sealed class Fixture : IAsyncDisposable {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public HomelabDb Db { get; }
        public Disk Disk { get; } = new() {Serial="A",Model="model"};
        public Fixture() { connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);Db.Database.EnsureCreated();var nas=new NasConnection{Enabled=false};Db.Connections.Add(nas);Disk.ConnectionId=nas.Id;Db.Disks.Add(Disk);Db.SaveChanges(); }
        public async Task Import(SmartReportPreview preview) {
            var report=new SmartReport { DiskId=Disk.Id,ReportHash=Guid.NewGuid().ToString(),ImportedUtc=Now,Actor="fixture" };
            Db.SmartReports.Add(report);await SelfTestFindings.StageAsync(Db,Disk,report,preview);await Db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync();await connection.DisposeAsync(); }
    }
    [Theory][InlineData(0)][InlineData(16)][InlineData(32)][InlineData(144)][InlineData(240)]
    public async Task CompletionAbortsUnknownAndProgressDoNotFlagFailure(int status) {
        await using var f=new Fixture();await f.Import(Preview(status:status));Assert.Empty(await f.Db.Findings.ToListAsync());Assert.Empty(await f.Db.SelfTestEvidence.ToListAsync());
    }
    [Fact] public async Task UnverifiedFailureWaitsForVerifiedEvidence() {
        await using var f=new Fixture();await f.Import(Preview(verified:false));Assert.Empty(await f.Db.Findings.ToListAsync());
        await f.Import(Preview());Assert.Single(await f.Db.Findings.ToListAsync());
    }
    [Fact] public async Task RepeatAndSuccessNeverReopenOrResolveButChangedEvidenceNeedsReview() {
        await using var f=new Fixture();await f.Import(Preview());var first=await f.Db.Findings.SingleAsync();
        await f.Import(Preview(1,status:0));Assert.Equal("open",first.State);
        FindingLifecycle.Resolve(first,Now,"Reviewed historical failure and recovery proof.");await f.Db.SaveChangesAsync();
        await f.Import(Preview(2));Assert.Single(await f.Db.Findings.ToListAsync());Assert.Equal("resolved",first.State);
        await f.Import(Preview(3,hours:101));Assert.Equal(2,await f.Db.Findings.CountAsync());
        var active=await f.Db.Findings.SingleAsync(x=>x.State=="open");Assert.Equal("review",active.Severity);Assert.Equal(f.Disk.Id,active.DiskId);Assert.Contains("historical",active.Evidence);
        Assert.Equal(2,await f.Db.SelfTestEvidence.CountAsync());
        var review=Planning.Review(f.Disk,[active],[],new(),Now)!;Assert.Contains("self-test failure",review.NextStep);
    }
    [Fact] public async Task BackfillDoesNotReplaceNewerEvidenceOrReopenReviewedEpisode() {
        await using var f=new Fixture();await f.Import(Preview(10));var first=await f.Db.Findings.SingleAsync();var evidence=first.Evidence;
        await f.Import(Preview(5,hours:50));Assert.Equal(evidence,first.Evidence);
        FindingLifecycle.Resolve(first,Now,"Reviewed");await f.Db.SaveChangesAsync();await f.Import(Preview(3,hours:30));
        Assert.Single(await f.Db.Findings.ToListAsync());Assert.Equal("resolved",first.State);Assert.Equal(3,await f.Db.SelfTestEvidence.CountAsync());
    }
    [Fact] public async Task RepeatedEvidenceAcrossSectionsIsNotCountedAsSeparatePhysicalTests() {
        await using var f=new Fixture();var preview=Preview();var standard=preview.AtaSelfTests![0];
        preview=preview with { AtaSelfTests=[standard,standard with {Source="ata_smart_self_test_log.extended"}] };
        await f.Import(preview);Assert.Single(await f.Db.SelfTestEvidence.ToListAsync());Assert.Single(await f.Db.Findings.ToListAsync());
        Assert.Contains("1 distinct failure evidence",(await f.Db.Findings.SingleAsync()).Evidence);
    }
    [Fact] public async Task MissingTimeDoesNotCreateActionableFailure() {
        await using var f=new Fixture();await f.Import(Preview() with {ObservedUtc=null});Assert.Empty(await f.Db.Findings.ToListAsync());
    }
}
