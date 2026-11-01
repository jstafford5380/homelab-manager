using System.Text.Json;
using Homelab.Api;
namespace Homelab.Tests;
public sealed class VdevChangesTests
{
    private static readonly DateTime Now=new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static readonly Guid Revision=Guid.NewGuid();
    private static readonly Guid Nas=Guid.NewGuid();
    private static PoolSnapshot Row(int seconds,long? errors=0,double? uptime=null) => new() {
        ConnectionId=Nas,PoolGuid="pool",FetchedUtc=Now.AddSeconds(seconds+2),
        SourceContextJson=VdevChanges.CaptureContext(JsonSerializer.SerializeToElement(new {system_serial="NAS-SERIAL",version="TrueNAS-SCALE-24.10.2",uptime_seconds=uptime??10000+seconds}),Revision,Now.AddSeconds(seconds),Now.AddSeconds(seconds+1)),
        VdevEvidenceJson=VdevEvidence.Capture(JsonSerializer.SerializeToElement(new {topology=new {data=new[]{new {guid="member",type="DISK",stats=new {read_errors=errors,write_errors=0,checksum_errors=0}}}}}))
    };
    [Fact] public void ReportsObservedIncreaseAndUnknownSeparately()
    { var change=VdevChanges.Compare(Row(60,7),Row(0,2));Assert.Equal("compared",change.Status);Assert.Equal(new("increased",5),Assert.Single(change.Members).Read);
      change=VdevChanges.Compare(Row(60,null),Row(0,2));Assert.Equal("compared",change.Status);Assert.Equal(new("unknown",null),Assert.Single(change.Members).Read); }
    [Fact] public void CounterDecreaseSuppressesAllDifferencesAndDoesNotImplyRepair()
    { var change=VdevChanges.Compare(Row(60,0),Row(0,7));Assert.Equal("counter_decreased",change.Status);Assert.Empty(change.Members);Assert.Contains("does not establish repair",change.Explanation); }
    [Theory][InlineData(50)][InlineData(10010)][InlineData(10100)]
    public void UptimeDiscontinuityBlocksComparison(double uptime)
    { Assert.Equal("uptime_boundary",VdevChanges.Compare(Row(60,7,uptime),Row(0)).Status); }
    [Fact] public void LegacyAndMissingPreviousRemainUnknown()
    { var row=Row(60);Assert.Equal("baseline",VdevChanges.Compare(row,null).Status);row.SourceContextJson=null;Assert.Equal("unknown",VdevChanges.Compare(row,Row(0)).Status); }
    [Fact] public void ChangedMemberAndConfigurationAreBoundaries()
    { var row=Row(60);row.VdevEvidenceJson=row.VdevEvidenceJson!.Replace("member","replacement");Assert.Equal("context_changed",VdevChanges.Compare(row,Row(0)).Status);
      row=Row(60);row.SourceContextJson=row.SourceContextJson!.Replace(Revision.ToString(),Guid.NewGuid().ToString());Assert.Equal("context_changed",VdevChanges.Compare(row,Row(0)).Status); }
    [Theory][InlineData(0)][InlineData(-60)][InlineData(3601)]
    public void InvalidOrLongIntervalsAreNotCompared(int seconds)
    { Assert.Equal("timing_gap",VdevChanges.Compare(Row(seconds),Row(0)).Status); }
    [Fact] public void KnownDriveIdentityChangeBlocksComparisonButMissingMappingDoesNotGuessOne()
    {
        void Map(PoolSnapshot row,Guid id) {
            var evidence=VdevEvidence.Read(row.VdevEvidenceJson!);
            row.VdevEvidenceJson=JsonSerializer.Serialize(evidence with {Nodes=evidence.Nodes.Select(x=>x with {DriveMapping=new VdevDriveMapping("inferred",id,"serial",null,"model","fixture")}).ToArray()},new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        var before=Row(0);var current=Row(60,7);var disk=Guid.NewGuid();Map(before,disk);
        Assert.Equal("compared",VdevChanges.Compare(current,before).Status);
        Map(current,disk);Assert.Equal("compared",VdevChanges.Compare(current,before).Status);
        Map(current,Guid.NewGuid());Assert.Equal("context_changed",VdevChanges.Compare(current,before).Status);
    }
    [Fact] public void ChangedSerialAndSoftwareAreBoundaries()
    { foreach(var field in new[]{"NAS-SERIAL","TrueNAS-SCALE-24.10.2"}) {var row=Row(60);row.SourceContextJson=row.SourceContextJson!.Replace(field,"different");Assert.Equal("context_changed",VdevChanges.Compare(row,Row(0)).Status);} }
}
