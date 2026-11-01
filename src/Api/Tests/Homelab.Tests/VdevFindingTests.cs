using System.Text.Json;
using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public sealed class VdevFindingTests
{
    private sealed class Fixture:IAsyncDisposable {
        public SqliteConnection Connection {get;}=new("Data Source=:memory:");
        public HomelabDb Db {get;}
        public NasConnection Nas {get;}=new();
        private readonly DateTime start=DateTime.UtcNow.AddHours(-1);
        public Fixture(){Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);}
        public async Task Init(){await Db.Database.MigrateAsync();Db.Connections.Add(Nas);await Db.SaveChangesAsync();}
        public PoolSnapshot Row(int minute,long? errors)=>new() {ConnectionId=Nas.Id,PoolGuid="pool",Name="tank",FetchedUtc=start.AddMinutes(minute).AddSeconds(2),
            SourceContextJson=VdevChanges.CaptureContext(JsonSerializer.SerializeToElement(new {system_serial="NAS-ONE",version="24.10.2",uptime_seconds=10000+minute*60}),Nas.ConfigurationRevision,start.AddMinutes(minute),start.AddMinutes(minute).AddSeconds(1)),
            VdevEvidenceJson=VdevEvidence.Capture(JsonSerializer.SerializeToElement(new {topology=new {data=new[]{new {guid="zfs-member",type="DISK",stats=new {read_errors=errors,write_errors=0,checksum_errors=0}}}}}))};
        public async Task Record(PoolSnapshot row){await VdevFindings.RecordAsync(Db,row);Db.PoolSnapshots.Add(row);await Db.SaveChangesAsync();}
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
    }
    [Fact]public async Task IncreaseCreatesPoolReviewWithRetainedBeforeAfterEvidence()
    {await using var f=new Fixture();await f.Init();await f.Record(f.Row(0,2));Assert.Empty(await f.Db.Findings.ToListAsync());await f.Record(f.Row(1,7));
     var finding=await f.Db.Findings.SingleAsync();Assert.Null(finding.DiskId);Assert.Equal("review",finding.Severity);Assert.Contains("read 2 → 7 (+5)",finding.Evidence);Assert.Contains("zfs-member",finding.Evidence);Assert.Contains("not guaranteed new errors",finding.Evidence);Assert.Contains("controller",finding.NextStep);Assert.Equal("open",finding.State);
     Assert.Equal(1,await f.Db.Activity.CountAsync());}
    [Fact]public async Task StableMissingResetAndChangedContextDoNotClearFinding()
    {await using var f=new Fixture();await f.Init();await f.Record(f.Row(0,0));await f.Record(f.Row(1,7));var finding=await f.Db.Findings.SingleAsync();var evidence=finding.Evidence;
     await f.Record(f.Row(2,7));await f.Record(f.Row(3,null));await f.Record(f.Row(4,0));var changed=f.Row(5,20);changed.VdevEvidenceJson=changed.VdevEvidenceJson!.Replace("zfs-member","replacement");await f.Record(changed);
     Assert.Equal("open",finding.State);Assert.Equal(evidence,finding.Evidence);Assert.Equal(1,await f.Db.Findings.CountAsync());Assert.Equal(1,await f.Db.Activity.CountAsync());}
    [Fact]public async Task NewerIncreaseUpdatesExistingAcknowledgedEpisode()
    {await using var f=new Fixture();await f.Init();await f.Record(f.Row(0,0));await f.Record(f.Row(1,7));var finding=await f.Db.Findings.SingleAsync();finding.State="acknowledged";await f.Db.SaveChangesAsync();await f.Record(f.Row(2,9));
     Assert.Equal(1,await f.Db.Findings.CountAsync());Assert.Equal("acknowledged",finding.State);Assert.Contains("read 7 → 9 (+2)",finding.Evidence);Assert.Equal(2,await f.Db.Activity.CountAsync());}
    [Fact]public async Task ManualResolutionIsNotReopenedByReplayButNewIncreaseCreatesEpisode()
    {await using var f=new Fixture();await f.Init();var baseline=f.Row(0,0);await f.Record(baseline);var current=f.Row(1,7);await f.Record(current);var finding=await f.Db.Findings.SingleAsync();FindingLifecycle.Resolve(finding,DateTime.UtcNow,"Reviewed recovery and native evidence.");await f.Db.SaveChangesAsync();
     // Replay the same pair with only its predecessor retained.
     f.Db.PoolSnapshots.Remove(current);await f.Db.SaveChangesAsync();await VdevFindings.RecordAsync(f.Db,current);await f.Db.SaveChangesAsync();Assert.Equal(1,await f.Db.Findings.CountAsync());
     await f.Record(f.Row(1,7));await f.Record(f.Row(2,7));Assert.Equal(1,await f.Db.Findings.CountAsync());await f.Record(f.Row(3,9));Assert.Equal(2,await f.Db.Findings.CountAsync());Assert.Equal("resolved",finding.State);Assert.Equal(1,await f.Db.Findings.CountAsync(x=>x.State=="open"));}
    [Fact]public async Task CollectionRollbackDiscardsFindingAuditAndSnapshotTogether()
    {await using var f=new Fixture();await f.Init();await f.Record(f.Row(0,0));await using(var transaction=await f.Db.Database.BeginTransactionAsync()){await f.Record(f.Row(1,7));await transaction.RollbackAsync();}
     f.Db.ChangeTracker.Clear();Assert.Empty(await f.Db.Findings.ToListAsync());Assert.Empty(await f.Db.Activity.ToListAsync());Assert.Equal(1,await f.Db.PoolSnapshots.CountAsync());}
    [Fact]public async Task UptimeBoundaryDoesNotCreateFinding()
    {await using var f=new Fixture();await f.Init();await f.Record(f.Row(0,0));var row=f.Row(1,7);row.SourceContextJson=row.SourceContextJson!.Replace("10060","10");await f.Record(row);Assert.Empty(await f.Db.Findings.ToListAsync());}
}
