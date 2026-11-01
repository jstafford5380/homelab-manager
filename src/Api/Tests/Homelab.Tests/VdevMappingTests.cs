using System.Text.Json;
using Homelab.Api;
namespace Homelab.Tests;
public sealed class VdevMappingTests
{
    private static readonly Disk Disk=new(){Serial="SERIAL-ONE",Model="MODEL",Wwn="5000c50012345678",Alias="sda"};
    private static readonly IdentityCandidate Candidate=new(Disk.Serial,Disk.Model,Disk.Wwn,"sda");
    private static string Evidence(string alias="sda",string state="ONLINE",string? device="sda1")=>VdevEvidence.Capture(JsonSerializer.SerializeToElement(new {topology=new {data=new[]{new {guid="member",type="DISK",status=state,path="/dev/disk/by-partuuid/fixture",device,disk=alias,stats=new {read_errors=7}}}}}));
    private static VdevDriveMapping Mapping(string evidence,IReadOnlyList<IdentityCandidate>? candidates=null,IReadOnlyList<Disk>? disks=null)=>Assert.Single(VdevEvidence.Read(VdevMapping.Attach(evidence,candidates,disks??[Disk])).Nodes).DriveMapping!;
    [Fact]public void MapsExplicitPartitionChainToFreshUniqueIdentityWithoutVerifyingBay()
    {var m=Mapping(Evidence(),[Candidate]);Assert.Equal("inferred",m.Status);Assert.Equal(Disk.Id,m.DiskId);Assert.Equal(Disk.Serial,m.Serial);Assert.Contains("not physical bay verification",m.Explanation);Assert.False(Disk.BayVerified);}
    [Fact]public void DoesNotGuessPartitionSuffixOrUseOldInventory()
    {Assert.Equal("unavailable",Mapping(Evidence()).Status);Assert.Equal("unavailable",Mapping(Evidence("sda1"),[Candidate]).Status);Assert.Equal("unavailable",Mapping(Evidence(device:null),[Candidate]).Status);}
    [Theory][InlineData("OFFLINE")][InlineData("UNAVAIL")][InlineData("DEGRADED")]
    public void NonOnlineMemberDoesNotClaimCurrentDrive(string state)
    {Assert.Null(Mapping(Evidence(state:state),[Candidate]).DiskId);}
    [Fact]public void DuplicateAliasesAndConflictingIdentityStayAmbiguous()
    {Assert.Equal("ambiguous",Mapping(Evidence(),[Candidate,Candidate]).Status);var other=new Disk{Serial=Disk.Serial,Model=Disk.Model,Wwn="5000c50087654321"};Assert.Equal("ambiguous",Mapping(Evidence(),[Candidate],[Disk,other]).Status);Assert.Equal("ambiguous",Mapping(Evidence(),[Candidate],[other]).Status);}
    [Fact]public void ReusedAliasFollowsFreshIdentityAndDoesNotRewriteOldSnapshot()
    {var old=Mapping(Evidence(),[Candidate]);var replacement=new Disk{Serial="REPLACEMENT",Model="OTHER",Wwn="5000c50087654321",Alias="sda"};var newer=Mapping(Evidence(),[new(replacement.Serial,replacement.Model,replacement.Wwn,"sda")],[Disk,replacement]);Assert.Equal(Disk.Id,old.DiskId);Assert.Equal(replacement.Id,newer.DiskId);}
    [Fact]public void MultipathAliasesCanResolveOneCompatibleWwn()
    {var other=Candidate with {Alias="sdb"};Assert.Equal(Disk.Id,Mapping(Evidence("sdb"),[Candidate,other]).DiskId);}
    [Fact]public void ExplicitlyAmbiguousDiskCannotBeLinked()
    {var disk=new Disk{Serial=Disk.Serial,Model=Disk.Model,Wwn=Disk.Wwn,MappingConfidence="ambiguous"};Assert.Equal("ambiguous",Mapping(Evidence(),[Candidate],[disk]).Status);}
    [Fact]public void DailySummaryRetainsHistoricalInferredIdentity()
    {
        var mapped=VdevMapping.Attach(Evidence(),[Candidate],[Disk]);
        var json=VdevDailyEvidence.Add(VdevDailyEvidence.Add(null,mapped),mapped)!;
        var daily=JsonSerializer.Deserialize<VdevDailySnapshot>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var row=Assert.Single(daily.Nodes);Assert.Equal(Disk.Id,row.Node.DriveMapping!.DiskId);Assert.Equal(2,row.Read.KnownCount);Assert.Equal(7,row.Read.Max);
    }
    [Fact]public void MissingMappingPreservesLegacyContextEncoding()
    {Assert.DoesNotContain("driveMapping",VdevEvidence.Context(Evidence())!);}
    [Fact]public void EnrichmentPreservesCountersAndChangesComparisonContextWhenIdentityChanges()
    {var original=Evidence();var mapped=VdevMapping.Attach(original,[Candidate],[Disk]);Assert.Equal(7,Assert.Single(VdevEvidence.Read(mapped).Nodes).ReadErrors);Assert.NotEqual(VdevEvidence.Context(original),VdevEvidence.Context(mapped));}
}
