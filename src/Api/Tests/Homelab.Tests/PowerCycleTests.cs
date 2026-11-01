using System.Text.Json.Nodes;
using Homelab.Api;
namespace Homelab.Tests;

public sealed class PowerCycleTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static JsonObject Report(string protocol) => new() {
        ["json_format_version"] = new JsonArray(1,0), ["smartctl"] = new JsonObject { ["version"] = new JsonArray(7,4), ["exit_status"] = 0 },
        ["device"] = new JsonObject { ["protocol"] = protocol }, ["serial_number"] = "SERIAL", ["model_name"] = "model",
        ["local_time"] = new JsonObject { ["time_t"] = new DateTimeOffset(Now.AddMinutes(-1)).ToUnixTimeSeconds() }, ["power_cycle_count"] = 42
    };
    private static SmartReportPreview Parse(JsonObject root) => SmartReports.Parse(new Disk { Serial="SERIAL", Model="model" }, new(root.ToJsonString()), Now);
    [Theory] [InlineData("ATA", "-v5")] [InlineData("NVMe", "-v5")]
    public void CuratedCountIsImportedWithoutGuessingVendorAttributes(string protocol, string schema)
    {
        var report = Parse(Report(protocol)); var reading = Assert.Single(report.Readings, x => x.Metric == "power_cycles");
        Assert.Equal(42, reading.Value); Assert.Equal("cycles", reading.Unit); Assert.Equal("observed", reading.Quality);
        Assert.EndsWith(schema, report.SourceSchema);
        Assert.Contains(report.Warnings, x => x.Contains("not a count of unsafe shutdowns"));
    }
    [Theory] [InlineData("null")] [InlineData("-1")] [InlineData("1.5")] [InlineData("\"42\"")] [InlineData("9007199254740992")] [InlineData("18446744073709551616")]
    public void InvalidOrInexactCycleCountStaysUnknown(string raw)
    {
        var root = Report("NVMe"); root["power_cycle_count"] = JsonNode.Parse(raw);
        var reading = Assert.Single(Parse(root).Readings, x => x.Metric == "power_cycles");
        Assert.Null(reading.Value); Assert.Equal("unknown", reading.Quality); Assert.NotNull(reading.MissingReason);
    }
    [Fact] public void MissingCuratedFieldDoesNotUseRawVendorOrUnsafeShutdownValues()
    {
        var root = Report("ATA"); root.Remove("power_cycle_count");
        root["ata_smart_attributes"] = JsonNode.Parse("{\"table\":[{\"id\":12,\"name\":\"Power_Cycle_Count\",\"raw\":{\"value\":42}}]}");
        root["nvme_smart_health_information_log"] = new JsonObject { ["unsafe_shutdowns"] = 4, ["power_cycles"] = 42 };
        Assert.Null(Assert.Single(Parse(root).Readings, x => x.Metric == "power_cycles").Value);
    }
    [Theory] [InlineData("ATA")] [InlineData("NVMe")]
    public void PriorVersionHashesRemainStableWhenNewlySupportedFieldChanges(string protocol)
    {
        var root = Report(protocol); var first = Parse(root); root["power_cycle_count"] = 43; var next = Parse(root);
        Assert.NotEqual(first.ReportHash, next.ReportHash);
        Assert.Equal(first.LegacyReportHash, next.LegacyReportHash); Assert.Equal(first.PreviousReportHash, next.PreviousReportHash);
        Assert.NotEqual(first.ReportHash, first.PreviousReportHash);
    }
    [Fact] public void UndatedOrCommandErrorReadingsRemainUnverified()
    {
        var root = Report("ATA"); root.Remove("local_time");
        Assert.Equal("unverified", Assert.Single(Parse(root).Readings, x => x.Metric == "power_cycles").Quality);
        root = Report("NVMe"); root["smartctl"]!["exit_status"] = 2;
        Assert.Equal("unverified", Assert.Single(Parse(root).Readings, x => x.Metric == "power_cycles").Quality);
    }
    [Theory] [InlineData(0, true)] [InlineData(9007199254740991d, true)] [InlineData(-1, false)] [InlineData(1.5, false)]
    public void ManualCyclesRequireExactNonnegativeCountsAndDoNotPredictFailure(double value, bool valid)
    {
        var current = new Observation { Metric="power_cycles", Unit="cycles", Value=value, Quality="observed", ObservedUtc=Now.AddMinutes(-1), Source="fixture", SourceKey="one", SourceSchema="cycles-v1", ResetEpoch="one" };
        Assert.Equal(valid, ObservationValidation.Validate(current, Now) is null);
        Assert.Null(HealthRules.Evaluate(current, null));
        current.Unit = "errors"; Assert.NotNull(ObservationValidation.Validate(current, Now));
    }
}
