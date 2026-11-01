using System.Text.Json;
using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Tests;

public sealed class PoolHistoryTests
{
    [Fact]
    public void PreservesByteCountsAndFetchTimeWithoutInventingScanTime()
    {
        using var json = JsonDocument.Parse("""{"guid":"18446744073709551615","name":"tank","status":"ONLINE","size":18000000000000,"allocated":12000000000000,"free":6000000000000,"scan":{"function":"SCRUB","state":"SCANNING","percentage":45.3,"errors":0,"secret":"discard"}}""");
        var now = DateTime.UtcNow;
        var snapshot = PoolHistory.Normalize(Guid.NewGuid(), json.RootElement, now)!;
        Assert.Equal("18446744073709551615", snapshot.PoolGuid);
        Assert.Equal(18000000000000L, snapshot.SizeBytes);
        Assert.Equal(6000000000000L, snapshot.FreeBytes);
        Assert.Equal(now, snapshot.FetchedUtc);
        Assert.Equal(45.3, snapshot.ScanPercentage);
        Assert.Equal(0L, snapshot.ScanErrors);
        Assert.DoesNotContain("discard", JsonSerializer.Serialize(snapshot));
    }

    [Theory]
    [InlineData("null", "null")]
    [InlineData("-1", "101")]
    [InlineData("\"100\"", "\"50\"")]
    [InlineData("1.5", "-1")]
    [InlineData("18446744073709551615", "null")]
    public void InvalidOrMissingMeasurementsStayUnknown(string count, string percent)
    {
        using var json = JsonDocument.Parse($$$"""{"guid":"pool","size":{{{count}}},"scan":{"errors":{{{count}}},"percentage":{{{percent}}}}}""");
        var snapshot = PoolHistory.Normalize(Guid.NewGuid(), json.RootElement, DateTime.UtcNow)!;
        Assert.Null(snapshot.SizeBytes); Assert.Null(snapshot.AllocatedBytes); Assert.Null(snapshot.FreeBytes);
        Assert.Null(snapshot.ScanPercentage); Assert.Null(snapshot.ScanErrors);
    }

    [Fact]
    public void SourceTimingIsSeparateFromFetchAndOnlyActiveScansHaveEstimates()
    {
        var fetched = DateTimeOffset.FromUnixTimeMilliseconds(1800000000000).UtcDateTime;
        using var active = JsonDocument.Parse("""{"guid":"pool","scan":{"state":"SCANNING","start_time":{"$date":1700000000000},"pause":{"$date":1700000001000},"end_time":{"$date":1700000002000},"total_secs_left":0}}""");
        var row = PoolHistory.Normalize(Guid.NewGuid(), active.RootElement, fetched)!;
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime, row.ScanStartedUtc);
        Assert.NotNull(row.ScanPausedUtc); Assert.Null(row.ScanEndedUtc);
        Assert.Equal(0, row.ScanRemainingSeconds); Assert.Equal(fetched, row.FetchedUtc);
        using var finished = JsonDocument.Parse("""{"guid":"pool","scan":{"state":"FINISHED","start_time":{"$date":1700000000000},"end_time":{"$date":1700000002000},"pause":{"$date":1700000001000},"total_secs_left":12}}""");
        row = PoolHistory.Normalize(Guid.NewGuid(), finished.RootElement, fetched)!;
        Assert.NotNull(row.ScanEndedUtc); Assert.Null(row.ScanPausedUtc); Assert.Null(row.ScanRemainingSeconds);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1700000000000")]
    [InlineData("\"2023-11-14T22:13:20Z\"")]
    [InlineData("{\"$date\":0}")]
    [InlineData("{\"$date\":-1}")]
    [InlineData("{\"$date\":1700000000000.5}")]
    [InlineData("{\"$date\":999999999999999999}")]
    [InlineData("{\"$date\":1900000000000}")]
    [InlineData("{\"$date\":1700000000000,\"extra\":true}")]
    public void InvalidOrFutureSourceTimesStayUnknown(string date)
    {
        using var json = JsonDocument.Parse($$$"""{"guid":"pool","scan":{"state":"SCANNING","start_time":{{{date}}},"pause":{{{date}}},"total_secs_left":-1}}""");
        var row = PoolHistory.Normalize(Guid.NewGuid(), json.RootElement, DateTimeOffset.FromUnixTimeMilliseconds(1800000000000).UtcDateTime)!;
        Assert.Null(row.ScanStartedUtc); Assert.Null(row.ScanPausedUtc); Assert.Null(row.ScanRemainingSeconds);
    }

    [Fact]
    public void ContradictoryChronologyDoesNotBecomeAnEvent()
    {
        using var json = JsonDocument.Parse("""{"guid":"pool","scan":{"state":"FINISHED","start_time":{"$date":1700000002000},"end_time":{"$date":1700000001000}}}""");
        var row = PoolHistory.Normalize(Guid.NewGuid(), json.RootElement, DateTimeOffset.FromUnixTimeMilliseconds(1800000000000).UtcDateTime)!;
        Assert.NotNull(row.ScanStartedUtc); Assert.Null(row.ScanEndedUtc);
    }

    [Theory]
    [InlineData("null", null)]
    [InlineData("123", null)]
    [InlineData("\"unrecognized text\"", null)]
    [InlineData("\"CORRUPT_DATA\"", "CORRUPT_DATA")]
    [InlineData("\"FUTURE_CODE\"", "FUTURE_CODE")]
    public void StatusCodesAreBoundedTokensWithoutFreeformDetails(string value, string? expected)
    {
        using var json = JsonDocument.Parse($$$"""{"guid":"pool","status_code":{{{value}}},"status_detail":"private file"}""");
        var row = PoolHistory.Normalize(Guid.NewGuid(), json.RootElement, DateTime.UtcNow)!;
        Assert.Equal(expected, row.StatusCode);
        Assert.DoesNotContain("private file", JsonSerializer.Serialize(row));
    }

    [Fact]
    public void MissingIdentityCannotMixDifferentPoolsInHistory()
    {
        using var json = JsonDocument.Parse("""{"name":"tank","size":100}""");
        Assert.Null(PoolHistory.Normalize(Guid.NewGuid(), json.RootElement, DateTime.UtcNow));
    }

    [Fact]
    public async Task MigrationRetainsHistoryAndScopesIdenticalPoolGuidsToTheirConnection()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options;
        await using (var db = new HomelabDb(options)) {
            await db.Database.MigrateAsync();
            var first = new NasConnection(); var second = new NasConnection();
            db.Connections.AddRange(first, second); await db.SaveChangesAsync();
            db.PoolSnapshots.AddRange(new PoolSnapshot { ConnectionId = first.Id, PoolGuid = "same", FreeBytes = 10, ScanStartedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ScanRemainingSeconds = 7200 }, new PoolSnapshot { ConnectionId = second.Id, PoolGuid = "same", FreeBytes = null });
            await db.SaveChangesAsync();
        }
        await using var reopened = new HomelabDb(options);
        var timed = await reopened.PoolSnapshots.SingleAsync(x => x.FreeBytes == 10);
        Assert.Equal(DateTimeKind.Utc, timed.ScanStartedUtc!.Value.Kind);
        Assert.Equal(7200, timed.ScanRemainingSeconds);
        Assert.Equal(2, await reopened.PoolSnapshots.CountAsync());
        Assert.Equal(2, await reopened.PoolSnapshots.Select(x => x.ConnectionId).Distinct().CountAsync());
        Assert.Single(await reopened.PoolSnapshots.Where(x => x.FreeBytes == null).ToListAsync());
    }
}
