using System.Text.Json;
using System.Text.Json.Nodes;
using Homelab.Api;
namespace Homelab.Tests;
public sealed class PoolRedundancyTests
{
    private static JsonObject Disk(string id, string status = "ONLINE") => new() { ["guid"]=id, ["type"]="DISK", ["status"]=status, ["children"]=new JsonArray() };
    private static JsonObject Group(string type, int count, string id = "vdev") => new() { ["guid"]=id, ["type"]=type, ["name"]="raidz2-0", ["status"]="ONLINE", ["children"]=new JsonArray(Enumerable.Range(0,count).Select(i => (JsonNode)Disk(id+"-"+i)).ToArray()) };
    internal static JsonObject Pool() => new() { ["guid"]="pool", ["status"]="ONLINE", ["topology"]=new JsonObject { ["data"]=new JsonArray(Group("MIRROR",2)), ["special"]=new JsonArray(), ["dedup"]=new JsonArray(), ["log"]=new JsonArray(), ["cache"]=new JsonArray(), ["spare"]=new JsonArray() } };
    private static RedundancyAssessment Assess(JsonObject root, bool fresh = true) => PoolRedundancy.Assess(JsonSerializer.SerializeToElement(root), fresh);
    [Theory] [InlineData("MIRROR",3,2)] [InlineData("RAIDZ1",3,1)] [InlineData("RAIDZ2",4,2)] [InlineData("RAIDZ3",5,3)] [InlineData("RAIDZ",4,2)]
    public void SimpleLayoutsExposeConfiguredPerGroupTolerance(string type, int members, int tolerance)
    {
        var pool=Pool(); pool["topology"]!["data"]=new JsonArray(Group(type,members)); var result=Assess(pool);
        Assert.Equal("layout recognized",result.Status); Assert.Equal(tolerance,Assert.Single(result.Groups).ConfiguredTolerance);
        Assert.Contains("not remaining safe removals", result.Explanation);
    }
    [Fact] public void SpecialAndDedupAreDataBearingAndSpareDoesNotAddTolerance()
    {
        var pool=Pool(); pool["topology"]!["special"]=new JsonArray(Disk("special")); pool["topology"]!["dedup"]=new JsonArray(Group("MIRROR",2,"dedup"));
        pool["topology"]!["spare"]=new JsonArray(Disk("spare","AVAIL")); pool["topology"]!["cache"]=new JsonArray(Disk("cache"));
        var result=Assess(pool); Assert.Equal(0,result.Groups.Single(x=>x.Role=="special").ConfiguredTolerance);
        Assert.Null(result.Groups.Single(x=>x.Role=="spare").ConfiguredTolerance); Assert.Null(result.Groups.Single(x=>x.Role=="cache").ConfiguredTolerance);
        Assert.NotEqual("layout recognized",result.Status);
    }
    [Fact] public void NonredundantDataRoleIsExplicit()
    {
        var pool=Pool(); pool["topology"]!["special"]=new JsonArray(Disk("special"));
        Assert.Equal("nonredundant data role",Assess(pool).Status);
    }
    [Fact] public void DegradedMemberDoesNotProduceRemainingRemovalAllowance()
    {
        var pool=Pool(); pool["topology"]!["data"]![0]!["children"]![0]!["status"]="FAULTED";
        var result=Assess(pool); Assert.Equal("review reported state",result.Status); Assert.Equal(1,result.Groups[0].ConfiguredTolerance);
        Assert.Contains("No remaining-failure allowance",result.Explanation);
    }
    [Theory] [InlineData("REPLACING")] [InlineData("DRAID")] [InlineData("STRIPE")] [InlineData("unknown")]
    public void UnsupportedLayoutsStayUnavailable(string type)
    {
        var pool=Pool(); pool["topology"]!["data"]![0]!["type"]=type; Assert.Equal("unavailable",Assess(pool).Status); Assert.Empty(Assess(pool).Groups);
    }
    [Fact] public void NestedReplacementAndDuplicateIdentityAreRejected()
    {
        var pool=Pool(); pool["topology"]!["data"]![0]!["children"]![0]=Group("REPLACING",2,"replacement"); Assert.Equal("unavailable",Assess(pool).Status);
        pool=Pool(); pool["topology"]!["data"]![0]!["children"]![1]!["guid"]="vdev-0"; Assert.Equal("unavailable",Assess(pool).Status);
    }
    [Fact] public void MissingRolesParityAndStatesRemainUnknown()
    {
        var pool=Pool(); pool["topology"]!.AsObject().Remove("special"); Assert.Equal("unavailable",Assess(pool).Status);
        pool=Pool(); pool["topology"]!["data"]=new JsonArray(Group("RAIDZ",4)); pool["topology"]!["data"]![0]!["name"]="raidz-unknown"; Assert.Equal("unavailable",Assess(pool).Status);
        pool=Pool(); pool["topology"]!["data"]![0]!["children"]![0]!.AsObject().Remove("status"); Assert.Equal("unavailable",Assess(pool).Status);
    }
    [Fact] public void StaleEvidenceAndMalformedStorageDoNotProduceAnAssessment()
    {
        Assert.Equal("unavailable",Assess(Pool(),false).Status); Assert.Empty(Assess(Pool(),false).Groups);
        Assert.Empty(PoolRedundancy.FromStored("{bad",true)); Assert.Empty(PoolRedundancy.FromStored("{}",true));
    }
    [Fact] public void MultipleVdevsKeepSeparateBudgets()
    {
        var pool=Pool(); pool["topology"]!["data"]!.AsArray().Add(Group("RAIDZ2",4,"second"));
        var result=Assess(pool); Assert.Equal(2,result.Groups.Length); Assert.Equal(new int?[]{1,2},result.Groups.Select(x=>x.ConfiguredTolerance));
        Assert.Contains("cannot be added",result.Explanation);
    }
}
