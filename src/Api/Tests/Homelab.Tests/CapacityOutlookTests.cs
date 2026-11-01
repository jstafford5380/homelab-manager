using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;

public class CapacityOutlookTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData("paused")][InlineData("unavailable")][InlineData("stale")][InlineData("insufficient")]
    [InlineData("suppressed")][InlineData("not-growing")][InlineData("variable")][InlineData("threshold-reached")]
    public void NonEstimatedStatesNeverExposeADate(string status)
    {
        var row = CapacityOutlook.FromEstimate(Guid.NewGuid(), "pool", "tank", new(30, status, "Reason", 31, 31, Now.AddDays(-30), Now, 1000, 800, 10, 0, .9));
        Assert.Null(row.ProjectedDateAtThreshold); Assert.Equal("unavailable", row.Confidence);
        Assert.Equal(.8, row.UsedFraction); Assert.Equal(status, row.Status);
    }
    [Fact] public void ProjectionIsAnchoredToSourceTimeAndUnknownCapacityStaysNull()
    {
        var estimate = new CapacityEstimate(30, "estimated", "Approximate", 31, 31, Now.AddDays(-30), Now, 1000, 400, 10, 40, 1);
        var row = CapacityOutlook.FromEstimate(Guid.NewGuid(), "pool", "tank", estimate);
        Assert.Equal(Now.AddDays(40), row.ProjectedDateAtThreshold);
        Assert.Equal("supported-trend", row.Confidence); Assert.Equal(30, row.BasisDays);
        Assert.Null(CapacityOutlook.FromEstimate(row.ConnectionId, "pool", "tank", estimate with { SizeBytes = null }).UsedFraction);
    }
    [Theory]
    [InlineData("fresh", "estimated")][InlineData("paused", "paused")][InlineData("denied", "unavailable")]
    [InlineData("stale", "unavailable")][InlineData("removed", "unverified")][InlineData("changed", "suppressed")]
    [InlineData("future", "unverified")]
    public async Task BatchedReaderUsesExistingEvidenceGuardsAndKeepsNasPoolIdentity(string scenario, string expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        Assert.Empty(await CapacityOutlook.ReadAsync(db, Now));
        var nas = new NasConnection { Enabled = scenario != "paused", TopologyJson = scenario == "removed" ? "[]" : """[{"guid":"same-guid"}]""" };
        var other = new NasConnection { Enabled = true, TopologyJson = """[{"guid":"same-guid"}]""" };
        db.Connections.AddRange(nas, other);
        db.Capabilities.AddRange(new Capability { ConnectionId = nas.Id, Name = "pools", Status = scenario == "denied" ? "denied" : "supported", ExpectedSeconds = 60, LastSuccessUtc = scenario == "stale" ? Now.AddHours(-1) : Now },
            new Capability { ConnectionId = other.Id, Name = "pools", Status = "supported", LastSuccessUtc = Now, ExpectedSeconds = 60 });
        for (var day = 0; day <= 30; day++) db.PoolSnapshots.Add(new() {
            ConnectionId = nas.Id, PoolGuid = "same-guid", Name = "tank", FetchedUtc = Now.AddDays(day - 30),
            SizeBytes = 1000000, AllocatedBytes = 100000 + day * 10000, Status = "ONLINE",
            TopologySignature = scenario == "changed" && day == 15 ? "changed" : "stable" });
        db.PoolSnapshots.Add(new() { ConnectionId = other.Id, PoolGuid = "same-guid", Name = "other", FetchedUtc = Now, SizeBytes = 2000000, AllocatedBytes = 100, Status = "ONLINE", TopologySignature = "stable" });
        if (scenario == "future") db.PoolSnapshots.Add(new() { ConnectionId = nas.Id, PoolGuid = "same-guid", Name = "tank", FetchedUtc = Now.AddHours(1) });
        await db.SaveChangesAsync();
        var rows = await CapacityOutlook.ReadAsync(db, Now);
        Assert.Equal(2, rows.Length);
        var row = Assert.Single(rows, x => x.ConnectionId == nas.Id);
        Assert.Equal(expected, row.Status); Assert.Equal(.4, row.UsedFraction); Assert.Equal(.8, row.ThresholdFraction);
        Assert.Equal(scenario == "fresh" ? Now.AddDays(40) : (DateTime?)null, row.ProjectedDateAtThreshold);
        Assert.Equal(Now, row.LastSampleUtc);
        var otherRow = Assert.Single(rows, x => x.ConnectionId == other.Id);
        Assert.Equal("insufficient", otherRow.Status); Assert.Null(otherRow.ProjectedDateAtThreshold);
        Assert.Equal(.00005, otherRow.UsedFraction);
    }
}
