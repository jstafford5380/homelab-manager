using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public class DriveEvidenceTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static Observation Reading(DateTime? at, string quality = "observed") => new() { Metric = "ata.pending_sectors", Unit = "sectors", Value = quality == "unknown" ? null : 3, ObservedUtc = at, ReceivedUtc = Now, Quality = quality, Source = "fixture", SourceSchema = "fixture-v1", SourceKey = Guid.NewGuid().ToString() };
    private static MetricEvidence Assess(Observation? submitted, Observation? observed) => DriveEvidence.Assess("ata.pending_sectors","Pending sectors",submitted,observed,Now,TimeSpan.FromMinutes(270));
    [Fact] public void RecentUploadCannotFreshenOldMeasurement() {
        var old = Reading(Now.AddDays(-10)); var result = Assess(old,old);
        Assert.Equal("stale",result.Freshness); Assert.Equal(old.ObservedUtc,result.LatestObserved!.ObservedUtc);
    }
    [Fact] public void MissingEvidenceIsUnknownNotZero() {
        var result = Assess(null,null); Assert.Equal("unknown",result.Freshness); Assert.Null(result.LatestObserved);
    }
    [Fact] public void UndatedValueCannotBecomeFresh() {
        var value = Reading(null,"unverified"); var result = Assess(value,null);
        Assert.Equal("unverified",result.Freshness); Assert.Null(result.LatestObserved); Assert.Equal(3,result.LatestSubmission!.Value);
    }
    [Theory][InlineData(269,"fresh")][InlineData(270,"fresh")][InlineData(271,"stale")]
    public void ConfiguredObservationAgeBoundaryIsExplicit(int minutes,string expected) {
        var value = Reading(Now.AddMinutes(-minutes)); Assert.Equal(expected,Assess(value,value).Freshness);
    }
    [Fact] public void FutureOrWrongUnitEvidenceCannotBeFresh() {
        var value = Reading(Now.AddMinutes(1)); Assert.Equal("unknown",Assess(value,value).Freshness);
        value.ObservedUtc = Now; value.Unit = "errors"; Assert.Equal("unknown",Assess(value,value).Freshness);
        value.Unit = "sectors"; value.Value = -1; Assert.Equal("unknown",Assess(value,value).Freshness);
    }
    [Fact] public void LaterMissingSubmissionDoesNotEraseEarlierObservedValue() {
        var earlier = Reading(Now.AddMinutes(-2)); var missing = Reading(Now,"unknown");
        var result = Assess(missing,earlier); Assert.Equal("fresh",result.Freshness);
        Assert.Equal(3,result.LatestObserved!.Value); Assert.Null(result.LatestSubmission!.Value);
        Assert.Contains("latest submission is unknown",result.Explanation);
    }
    [Fact] public async Task DatabaseSummarySeparatesReceiptOrderFromObservationOrderAndScopesDisk()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        var nas=new NasConnection{Enabled=false};db.Connections.Add(nas);var disk = new Disk{ConnectionId=nas.Id}; db.Disks.Add(disk);
        var recent = Reading(Now.AddMinutes(-5)); recent.ReceivedUtc = Now.AddMinutes(-3); recent.DiskId = disk.Id;
        var older = Reading(Now.AddDays(-10)); older.ReceivedUtc = Now.AddMinutes(-2); older.DiskId = disk.Id; older.Value = 8;
        var missing = Reading(Now,"unknown"); missing.DiskId = disk.Id;
        var unrelated = Reading(Now); unrelated.DiskId = Guid.NewGuid(); unrelated.Value = 999;
        db.Disks.Add(new(){Id=unrelated.DiskId!.Value,ConnectionId=nas.Id});
        foreach(var row in new[]{recent,older,missing,unrelated})row.ConnectionId=nas.Id;
        db.Observations.AddRange(recent,older,missing,unrelated);
        for (var i=0;i<1001;i++) { var temperature = Reading(Now); temperature.DiskId=disk.Id; temperature.ConnectionId=nas.Id; temperature.Metric="temperature"; temperature.Unit="°C"; temperature.Value=30; db.Observations.Add(temperature); }
        await db.SaveChangesAsync();
        var results = await DriveEvidence.ReadAsync(db,disk.Id,Now,90);
        Assert.Equal(17,results.Length);
        var pending = results.Single(x=>x.Metric=="ata.pending_sectors");
        Assert.Equal(missing.Id,pending.LatestSubmission!.Id); Assert.Equal(recent.Id,pending.LatestObserved!.Id);
        Assert.Equal("fresh",pending.Freshness); Assert.Equal(3,pending.LatestObserved.Value);
        Assert.Equal("unknown",results.Single(x=>x.Metric=="nvme.media_errors").Freshness);
        Assert.Equal("stale",(await DriveEvidence.ReadAsync(db,disk.Id,Now.AddHours(6),90)).Single(x=>x.Metric=="ata.pending_sectors").Freshness);
    }
}
