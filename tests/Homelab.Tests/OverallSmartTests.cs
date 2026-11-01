using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using Homelab.Api;
namespace Homelab.Tests;

public sealed class OverallSmartTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static JsonObject Report(string protocol = "ATA") => new() {
        ["json_format_version"] = new JsonArray(1,0),
        ["smartctl"] = new JsonObject { ["version"] = new JsonArray(7,4), ["exit_status"] = 0 },
        ["device"] = new JsonObject { ["protocol"] = protocol }, ["serial_number"] = "SERIAL", ["model_name"] = "model",
        ["local_time"] = new JsonObject { ["time_t"] = new DateTimeOffset(Now.AddMinutes(-1)).ToUnixTimeSeconds() },
        ["smart_status"] = new JsonObject { ["passed"] = true }
    };
    private static SmartReportPreview Parse(JsonObject root) => SmartReports.Parse(new Disk { Serial="SERIAL", Model="model" }, new(root.ToJsonString()), Now);

    [Theory] [InlineData("ATA", true)] [InlineData("ATA", false)] [InlineData("NVMe", true)] [InlineData("NVMe", false)]
    public void ExplicitBooleanIsRetainedWithSourceTime(string protocol, bool passed)
    {
        var root = Report(protocol); root["smart_status"]!["passed"] = passed;
        var result = Parse(root);
        Assert.Equal(passed, result.OverallSmart!.Passed); Assert.Equal("observed", result.OverallSmart.Quality);
        Assert.NotNull(result.ObservedUtc);
        Assert.Contains(result.Warnings, x => x.Contains("never overrides adverse counters"));
        if (!passed) Assert.Contains(result.Warnings, x => x.Contains("no failure date is inferred"));
    }
    [Theory] [InlineData("null")] [InlineData("0")] [InlineData("1")] [InlineData("\"true\"")] [InlineData("{}")]
    public void InvalidOrMissingValuesNeverBecomePassed(string value)
    {
        var root = Report(); root["smart_status"]!["passed"] = JsonNode.Parse(value);
        Assert.Null(Parse(root).OverallSmart!.Passed); Assert.Equal("unknown", Parse(root).OverallSmart!.Quality);
        root.Remove("smart_status"); Assert.Null(Parse(root).OverallSmart!.Passed);
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void CommandErrorsOrContradictoryPassRemainUnverified(int exit)
    {
        var root = Report(); root["smartctl"]!["exit_status"] = exit;
        Assert.True(Parse(root).OverallSmart!.Passed); Assert.Equal("unverified", Parse(root).OverallSmart!.Quality);
    }
    [Fact]
    public void UndatedResultIsNotCurrentEvidence()
    {
        var root = Report(); root.Remove("local_time");
        Assert.Equal("unverified", Parse(root).OverallSmart!.Quality);
    }
    [Fact]
    public void StatusChangesHashWithoutChangingLegacyFingerprintsOrNumericEvidence()
    {
        var root = Report("NVMe"); root["nvme_smart_health_information_log"] = new JsonObject { ["media_errors"] = 8 };
        var first = Parse(root); root["smart_status"]!["passed"] = false; var second = Parse(root);
        Assert.NotEqual(first.ReportHash, second.ReportHash);
        Assert.Equal(first.BeforeOverallReportHash, second.BeforeOverallReportHash);
        Assert.Equal(first.PreviousReportHash, second.PreviousReportHash);
        Assert.Equal(first.LegacyReportHash, second.LegacyReportHash);
        Assert.Equal(first.Readings, second.Readings);
        Assert.Equal(8, Assert.Single(first.Readings, x => x.Metric == "nvme.media_errors").Value);
    }
    [Fact]
    public void OlderCuratedReportsRemainReadableWithoutInventedStatus()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(Parse(Report())))!.AsObject();
        json.Remove("OverallSmart"); json.Remove("BeforeOverallReportHash");
        var older = JsonSerializer.Deserialize<SmartReportPreview>(json.ToJsonString())!;
        Assert.Null(older.OverallSmart); Assert.NotEmpty(older.Readings);
    }
    [Theory]
    [InlineData(0, "observed", false)]
    [InlineData(1, "observed", true)]
    [InlineData(1, "unverified", false)]
    [InlineData(1, "unknown", false)]
    public void FailureRuleRequiresObservedFailure(double value, string quality, bool expected)
    {
        var observation = new Observation { Metric = "smart.overall_failed", Value = value, Unit = "flag",
            Quality = quality, ObservedUtc = Now.AddMinutes(-1), Source = "fixture" };
        var rule = HealthRules.Evaluate(observation, null);
        Assert.Equal(expected, rule is not null);
        if (rule is not null) { Assert.Equal("urgent", rule.Severity); Assert.Contains("not a predicted failure date", rule.Evidence); }
        observation.ObservedUtc = null; Assert.Null(HealthRules.Evaluate(observation, null));
    }

    [Theory] [InlineData(-1)] [InlineData(0.5)] [InlineData(2)]
    public void FailureFlagRejectsNonBinaryValues(double value)
    {
        var input = new Observation { Metric = "smart.overall_failed", Value = value, Unit = "flag", Quality = "observed",
            ObservedUtc = Now.AddMinutes(-1), Source = "fixture", SourceKey = "one", SourceSchema = "test", ResetEpoch = "" };
        Assert.NotNull(ObservationValidation.Validate(input, Now));
    }

    [Fact]
    public async Task PassesDoNotResolveAndBackfilledFailuresCannotReopenReviewedEpisode()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var nas=new NasConnection{Enabled=false};db.Connections.Add(nas);var disk = new Disk{ConnectionId=nas.Id}; db.Disks.Add(disk); await db.SaveChangesAsync();
        async Task Import(int minute, int value, string source = "fixture") {
            await ObservationIngestion.StageAsync(db, disk, new Observation { Metric = "smart.overall_failed", Value = value,
                Unit = "flag", Quality = "observed", ObservedUtc = Now.AddMinutes(minute), Source = source,
                SourceSchema = "test", SourceKey = Guid.NewGuid().ToString(), ResetEpoch = "continuous" }, "owner");
            await db.SaveChangesAsync();
        }
        await Import(-30, 1); await Import(-20, 0); await Import(-10, 0);
        var finding = await db.Findings.SingleAsync(); Assert.Equal("open", finding.State);
        FindingLifecycle.Resolve(finding, Now, "Recovery and replacement evidence reviewed."); await db.SaveChangesAsync();
        await Import(-15, 1, "older report"); Assert.Equal("resolved", finding.State); Assert.Equal(1, await db.Findings.CountAsync());
        await Import(-1, 1); Assert.Equal(2, await db.Findings.CountAsync());
        Assert.Single(await db.Findings.Where(x => x.State == "open").ToListAsync());
    }
}
