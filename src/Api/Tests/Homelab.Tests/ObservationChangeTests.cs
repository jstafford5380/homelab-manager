using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public sealed class ObservationChangeTests
{
    private static readonly Guid NasId=Guid.NewGuid();
    private static readonly Guid DiskId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;
    private static Observation Row(long id, double value, int hours) => new() {
        Id=id,DiskId=DiskId,ConnectionId=NasId,Metric="ata.crc_errors",Unit="errors",Value=value,
        ObservedUtc=Now.AddHours(hours),Quality="observed",Source="Fixture report",SourceSchema="fixture-v1",SourceKey="fixture-"+id,ResetEpoch="operator-confirmed-baseline"
    };
    [Theory] [InlineData(7,5)] [InlineData(2,0)]
    public void ComparableCountersShowNetDifferencesWithoutPredictingHealth(double current, double difference) {
        var change=ObservationChanges.Compare(Row(2,current,-1),Row(1,2,-2),Now);
        Assert.Equal("compared",change.Status);Assert.Equal(difference,change.Difference);Assert.Equal(1,change.PreviousId);
        Assert.Contains("not a rate",change.Explanation);Assert.Contains("does not predict failure",change.Explanation);
    }
    [Theory]
    [InlineData("decrease","counter_decreased")][InlineData("unknown-epoch","continuity_unknown")][InlineData("changed-epoch","continuity_unknown")]
    [InlineData("source","context_changed")][InlineData("schema","context_changed")][InlineData("disk","context_changed")]
    [InlineData("nas","context_changed")][InlineData("unverified","unverified")][InlineData("missing","unverified")]
    [InlineData("same-time","context_changed")][InlineData("ambiguous","ambiguous_time")][InlineData("baseline","baseline")]
    public void BoundariesNeverInventNumericChanges(string boundary,string expected) {
        var current=Row(2,7,-1);Observation? previous=Row(1,2,-2);
        switch(boundary) {
            case "decrease":current.Value=0;break;
            case "unknown-epoch":current.ResetEpoch="";break;
            case "changed-epoch":current.ResetEpoch="new-baseline";break;
            case "source":current.Source="Other source";break;
            case "schema":current.SourceSchema="new-schema";break;
            case "disk":current.DiskId=Guid.NewGuid();break;
            case "nas":current.ConnectionId=Guid.NewGuid();break;
            case "unverified":previous.Quality="unverified";break;
            case "missing":previous.Value=null;previous.Quality="unknown";previous.MissingReason="Missing field";break;
            case "same-time":current.ObservedUtc=previous.ObservedUtc;break;
            case "baseline":previous=null;break;
        }
        var change=ObservationChanges.Compare(current,previous,Now,boundary=="ambiguous");Assert.Equal(expected,change.Status);Assert.Null(change.Difference);
    }
    [Fact] public void TemperatureDecreaseIsAGaugeChangeAndFlagsAreNotSubtracted() {
        var current=Row(2,-5,-1);var previous=Row(1,-2,-2);
        current.Metric=previous.Metric="temperature";current.Unit=previous.Unit="°C";
        Assert.Equal(-3,ObservationChanges.Compare(current,previous,Now).Difference);
        current.Metric=previous.Metric="nvme.critical_warning";current.Unit=previous.Unit="bitmask";current.Value=3;previous.Value=1;
        var flags=ObservationChanges.Compare(current,previous,Now);Assert.Equal("categorical",flags.Status);Assert.Null(flags.Difference);
    }
    [Fact] public async Task HistoryUsesChronologicalPredecessorAcrossPagesAndWindowButHonorsSnapshotCeiling() {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();db.Connections.Add(new(){Id=NasId,Enabled=false});db.Disks.Add(new(){Id=DiskId,ConnectionId=NasId});
        db.Observations.AddRange(Row(1,2,-48),Row(2,7,-1));await db.SaveChangesAsync();
        var first=await ObservationHistory.ReadAsync(db,DiskId,"ata.crc_errors",1,Now);
        Assert.Single(first.Items);Assert.Equal(5,first.Comparisons[2].Difference);Assert.Equal(1,first.Comparisons[2].PreviousId);
        db.Observations.Add(Row(3,4,-2));await db.SaveChangesAsync();
        var frozen=await ObservationHistory.ReadAsync(db,DiskId,"ata.crc_errors",1,Now,first.SnapshotId);Assert.Equal(5,frozen.Comparisons[2].Difference);
        var refreshed=await ObservationHistory.ReadAsync(db,DiskId,"ata.crc_errors",1,Now);Assert.Equal(3,refreshed.Comparisons[2].Difference);Assert.Equal(3,refreshed.Comparisons[2].PreviousId);
        var olderPage=await ObservationHistory.ReadAsync(db,DiskId,"ata.crc_errors",0,Now,refreshed.SnapshotId,3);Assert.Equal(3,olderPage.Comparisons[2].Difference);
        db.Observations.Add(Row(4,99,-2));await db.SaveChangesAsync();
        var ambiguous=await ObservationHistory.ReadAsync(db,DiskId,"ata.crc_errors",1,Now);Assert.Equal("ambiguous_time",ambiguous.Comparisons[2].Status);
        Assert.Equal("ambiguous_time",ambiguous.Comparisons[3].Status);Assert.Null(ambiguous.Comparisons[2].Difference);
    }
}
