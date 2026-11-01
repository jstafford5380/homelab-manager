using System.Text.Json;
using Homelab.Api;
namespace Homelab.Tests;
public sealed class VdevEvidenceTests
{
    private static string Capture(string topology) => VdevEvidence.Capture(JsonDocument.Parse("{\"topology\":"+topology+"}").RootElement);
    [Fact]
    public void PreservesLargeGuidNestedReplacementAndUnknownCounters()
    {
        var data=VdevEvidence.Read(Capture("""{"data":[{"guid":18446744073709551615,"type":"REPLACING","children":[{"guid":"child","type":"DISK","disk":"sda","stats":{"read_errors":0,"write_errors":-1,"checksum_errors":9007199254740992}}]}],"spare":[{"guid":"spare","type":"DISK"}]}"""));
        Assert.Equal("reported",data.Status); Assert.Equal(3,data.Nodes.Length);
        var child=Assert.Single(data.Nodes,x=>x.Guid=="child");
        Assert.Equal("18446744073709551615",child.ParentGuid);Assert.Equal("data",child.Role);Assert.Equal("sda",child.DiskAlias);
        Assert.Equal(0,child.ReadErrors);Assert.Null(child.WriteErrors);Assert.Null(child.ChecksumErrors);
        Assert.Equal("spare",Assert.Single(data.Nodes,x=>x.Guid=="spare").Role);
    }
    [Theory]
    [InlineData("{\"data\":[{\"guid\":\"a\",\"type\":\"DISK\"},{\"guid\":\"a\",\"type\":\"DISK\"}]}")]
    [InlineData("{\"data\":[],\"data\":[]}")]
    [InlineData("{\"future\":[]}")]
    [InlineData("{\"data\":[{\"guid\":\"a\",\"type\":\"DISK\",\"children\":{}}]}")]
    public void MalformedTopologyIsUnavailableWithoutPartialSuccess(string topology)
    { var data=VdevEvidence.Read(Capture(topology));Assert.Equal("unavailable",data.Status);Assert.Empty(data.Nodes); }
    [Fact]
    public void CanonicalContextExcludesCountersButRetainsIdentityStateAndAliases()
    {
        var a=Capture("""{"data":[{"guid":"b","type":"DISK","stats":{"read_errors":7}},{"guid":"a","type":"DISK"}]}""");
        var b=Capture("""{"data":[{"guid":"a","type":"DISK"},{"guid":"b","type":"DISK","stats":{"read_errors":0}}]}""");
        Assert.Equal(VdevEvidence.Context(a),VdevEvidence.Context(b));
        Assert.NotEqual(VdevEvidence.Context(a),VdevEvidence.Context(b.Replace("\"device\":null","\"device\":\"new-alias\"")));
        var summary=VdevDailyEvidence.Add(VdevDailyEvidence.Add(null,a),b)!;
        var parsed=JsonSerializer.Deserialize<VdevDailySnapshot>(summary,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(2,parsed.Samples);var node=Assert.Single(parsed.Nodes,x=>x.Node.Guid=="b");
        Assert.Equal(new VdevCounterRange(2,0,7),node.Read);Assert.Equal(new VdevCounterRange(0,null,null),node.Checksum);
        Assert.Null(node.Node.ReadErrors);
    }
    [Fact]
    public void DailyAggregationRejectsDifferentMemberContexts()
    {
        var a=Capture("""{"data":[{"guid":"a","type":"DISK"}]}""");
        var b=Capture("""{"data":[{"guid":"b","type":"DISK"}]}""");
        Assert.Throws<InvalidDataException>(()=>VdevDailyEvidence.Add(VdevDailyEvidence.Add(null,a),b));
    }
}
