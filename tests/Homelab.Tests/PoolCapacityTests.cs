using Homelab.Api;
using System.Text.Json;
namespace Homelab.Tests;
public class PoolCapacityTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static List<PoolSnapshot> Rows() => Enumerable.Range(0,31).Select(i => new PoolSnapshot {
        FetchedUtc = Now.AddDays(i-30), SizeBytes = 1000000, AllocatedBytes = 100000 + i*10000,
        TopologySignature = "stable", Status = "ONLINE"
    }).ToList();
    private static CapacityEstimate Evaluate(IEnumerable<PoolSnapshot> rows, int days = 30) {
        var window = new PoolCapacity.Window(days,Now,TimeSpan.FromMinutes(5));
        foreach(var row in rows) window.Add(row);
        return window.Finish();
    }
    [Fact] public void StableGrowthUsesMeasuredDailySlopeAndPlanningMarker() {
        var result = Evaluate(Rows()); Assert.Equal("estimated",result.Status);
        Assert.Equal(10000,result.GrowthBytesPerDay); Assert.Equal(40,result.DaysTo80Percent); Assert.Equal(1,result.Fit);
    }
    [Theory]
    [InlineData("topology")][InlineData("size")][InlineData("schema")][InlineData("missing")][InlineData("offline")][InlineData("deletion")][InlineData("gap")]
    public void DisruptionsSuppressEstimates(string change) {
        var rows = Rows();
        switch(change) {
            case "topology": rows[15].TopologySignature = "changed"; break;
            case "size": rows[15].SizeBytes = 2000000; break;
            case "schema": rows[15].SourceSchema = "other"; break;
            case "missing": rows[15].TopologySignature = null; break;
            case "offline": rows[15].Status = "OFFLINE"; break;
            case "deletion": rows[15].AllocatedBytes = 10000; break;
            case "gap": rows.RemoveRange(10,4); break;
        }
        var result = Evaluate(rows); Assert.Equal("suppressed",result.Status); Assert.Null(result.DaysTo80Percent);
    }
    [Fact] public void RecentDenseSamplesCannotStandInForLongHistory() {
        var rows = Rows(); foreach(var row in rows) row.FetchedUtc = Now.AddMinutes(-rows.IndexOf(row));
        Assert.Equal("insufficient",Evaluate(rows.OrderBy(x=>x.FetchedUtc)).Status);
    }
    [Fact] public void StaleEvidenceDoesNotForecast() => Assert.Equal("stale",Evaluate(Rows().SkipLast(1)).Status);
    [Fact] public void FlatUsageHasNoInventedExhaustionDate() {
        var rows = Rows(); foreach(var row in rows) row.AllocatedBytes = 100000;
        var result = Evaluate(rows); Assert.Equal("not-growing",result.Status); Assert.Null(result.DaysTo80Percent);
    }
    [Fact] public void WindowChoiceExcludesEarlierChanges() {
        var rows = Rows(); rows[0].TopologySignature = "old";
        Assert.Equal("suppressed",Evaluate(rows).Status); Assert.Equal("estimated",Evaluate(rows,7).Status);
    }
    [Fact] public void AlreadyAtPlanningMarkerIsExplicit() {
        var rows = Rows(); foreach(var row in rows) row.AllocatedBytes += 500000;
        var result = Evaluate(rows); Assert.Equal("threshold-reached",result.Status); Assert.Equal(0,result.DaysTo80Percent);
    }
    [Theory]
    [InlineData("denied")][InlineData("unavailable")][InlineData("unsupported")][InlineData("stale")][InlineData("missing")][InlineData("future")]
    public void UnverifiedCollectionWithholdsDatesButPreservesHistoricalValues(string status) {
        var connection = new NasConnection { TopologyJson = """[{"guid":"pool"}]""" };
        Capability? capability = status == "missing" ? null : new() { Status = status is "stale" or "future" ? "supported" : status,
            ExpectedSeconds = 60, LastSuccessUtc = status == "stale" ? Now.AddMinutes(-10) : status == "future" ? Now.AddHours(1) : Now };
        var raw = Evaluate(Rows()); var result = PoolCapacity.RequireCurrentContext(raw, connection, capability, "pool", Now);
        Assert.Equal("unavailable", result.Status); Assert.Null(result.DaysTo80Percent);
        Assert.Equal(raw.AllocatedBytes, result.AllocatedBytes); Assert.Equal(raw.Samples, result.Samples);
    }
    [Theory][InlineData(null)][InlineData("[]")][InlineData("bad json")][InlineData("[{\"guid\":\"other\"}]")]
    public void RemovedOrUnknownPoolMembershipWithholdsDates(string? topology) {
        var connection = new NasConnection { TopologyJson = topology };
        var capability = new Capability { Status = "supported", LastSuccessUtc = Now, ExpectedSeconds = 60 };
        var result = PoolCapacity.RequireCurrentContext(Evaluate(Rows()), connection, capability, "pool", Now);
        Assert.Equal("unverified", result.Status); Assert.Null(result.DaysTo80Percent);
    }
    [Fact] public void CurrentPoolCoverageAllowsTheExistingEvidencePolicyToDecide() {
        var connection = new NasConnection { TopologyJson = """[{"guid":"pool"}]""" };
        var capability = new Capability { Status = "supported", LastSuccessUtc = Now, ExpectedSeconds = 60 };
        var raw = Evaluate(Rows());
        Assert.Equal(raw, PoolCapacity.RequireCurrentContext(raw, connection, capability, "pool", Now));
        connection.Enabled = false;
        var paused = PoolCapacity.RequireCurrentContext(raw, connection, capability, "pool", Now);
        Assert.Equal("paused", paused.Status); Assert.Null(paused.DaysTo80Percent);
    }
    [Fact] public void TopologySignatureIgnoresCountersAndPathsButDetectsMembership() {
        string Topology(string guid,string path,int errors) => JsonSerializer.Serialize(new { topology = new { data = new[] { new { guid, type = "DISK", path, stats = new { read_errors = errors } } }, log = Array.Empty<object>(), cache = Array.Empty<object>(), spare = Array.Empty<object>(), special = Array.Empty<object>(), dedup = Array.Empty<object>() } });
        string? Signature(string json) { using var doc = JsonDocument.Parse(json); return PoolCapacity.TopologySignature(doc.RootElement); }
        var initial = Signature(Topology("123","/dev/a",0)); Assert.NotNull(initial);
        Assert.Equal(initial,Signature(Topology("123","/dev/b",10)));
        Assert.NotEqual(initial,Signature(Topology("456","/dev/a",0)));
        Assert.Null(Signature("{\"topology\":{\"data\":[]}}"));
    }
}
