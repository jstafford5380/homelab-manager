using System.Text.Json.Nodes;
using Homelab.Api;
namespace Homelab.Tests;
public sealed class SmartReportTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static Disk Disk() => new() { Serial = "SERIAL", Model = "model", MappingConfidence = "inferred" };
    private static JsonObject Report(string protocol = "ATA") => JsonNode.Parse("""
        {"json_format_version":[1,0],"smartctl":{"version":[7,4],"exit_status":0},"device":{"protocol":"ATA"},"serial_number":"SERIAL","model_name":"model","local_time":{"time_t":1793491200},
         "temperature":{"current":32},"power_on_time":{"hours":24000},"ata_smart_attributes":{"table":[
         {"id":197,"name":"Current_Pending_Sector","value":100,"worst":99,"thresh":10,"when_failed":"","raw":{"value":4,"string":"4"}}]},
         "nvme_smart_health_information_log":{"media_errors":12},"private_output":"must not be stored"}
        """)!.AsObject().WithProtocol(protocol);
    [Fact] public void RawAndNormalizedEvidenceStaySeparateAndAtaRulesRequireUnitConfirmation()
    {
        var json = Report().ToJsonString();
        var unverified = SmartReports.Parse(Disk(), new(json), Now);
        var pending = Assert.Single(unverified.Readings, x => x.Metric == "ata.pending_sectors");
        Assert.Equal(4, pending.Value); Assert.Equal("unverified", pending.Quality);
        var confirmed = SmartReports.Parse(Disk(), new(json, "epoch", true), Now);
        Assert.Equal("observed", Assert.Single(confirmed.Readings, x => x.Metric == "ata.pending_sectors").Quality);
        var attr = Assert.Single(confirmed.Attributes, x => x.Id == 197); Assert.Equal(100, attr.NormalizedValue); Assert.Equal("4", attr.RawNumeric);
        Assert.Equal("unknown", Assert.Single(confirmed.Readings, x => x.Metric == "ata.reallocated_sectors").Quality);
        Assert.Equal(unverified.ReportHash, confirmed.ReportHash);
        Assert.DoesNotContain("private_output", System.Text.Json.JsonSerializer.Serialize(confirmed));
    }
    [Fact] public void NvmeCountsAndToolCuratedValuesDoNotDependOnAtaConfirmation()
    {
        var result = SmartReports.Parse(Disk(), new(Report("NVMe").ToJsonString(), "epoch"), Now);
        Assert.Equal(8, result.Readings.Length); Assert.Empty(result.Attributes);
        var media = Assert.Single(result.Readings, x => x.Metric == "nvme.media_errors"); Assert.Equal(12, media.Value); Assert.Equal("observed", media.Quality);
        Assert.Equal(24000, Assert.Single(result.Readings, x => x.Metric == "power_on_hours").Value);
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(7)]
    public void CommandErrorsPreventRuleInputButDoNotZeroFill(int status)
    {
        var root = Report(); root["smartctl"]!["exit_status"] = status;
        var result = SmartReports.Parse(Disk(), new(root.ToJsonString(), "epoch", true), Now);
        Assert.All(result.Readings.Where(x => x.Value is not null), x => Assert.Equal("unverified", x.Quality));
    }
    [Fact] public void HealthFailureExitBitsDoNotHideAdverseEvidence()
    {
        var root = Report(); root["smartctl"]!["exit_status"] = 8;
        var result = SmartReports.Parse(Disk(), new(root.ToJsonString(), "epoch", true), Now);
        Assert.Equal("observed", Assert.Single(result.Readings, x => x.Metric == "ata.pending_sectors").Quality);
    }
    [Fact] public void MissingTimeRemainsUnverifiedAndFutureOrNonNumericTimeIsRejected()
    {
        var root = Report(); root.Remove("local_time");
        var result = SmartReports.Parse(Disk(), new(root.ToJsonString(), "epoch", true), Now);
        Assert.Null(result.ObservedUtc); Assert.All(result.Readings.Where(x => x.Value is not null), x => Assert.Equal("unverified", x.Quality));
        root["local_time"] = new JsonObject { ["time_t"] = "yesterday" }; Assert.Throws<InvalidDataException>(() => SmartReports.Parse(Disk(), new(root.ToJsonString()), Now));
        root["local_time"]!["time_t"] = new DateTimeOffset(Now.AddDays(1)).ToUnixTimeSeconds(); Assert.Throws<InvalidDataException>(() => SmartReports.Parse(Disk(), new(root.ToJsonString()), Now));
    }
    [Fact] public void UnsupportedFieldNamesAndHugeCountsNeverBecomeZeroOrRoundedValues()
    {
        var root = Report(); root["ata_smart_attributes"]!["table"]![0]!["name"] = "Vendor_Custom_Value";
        var result = SmartReports.Parse(Disk(), new(root.ToJsonString(), "epoch", true), Now);
        Assert.Null(Assert.Single(result.Readings, x => x.Metric == "ata.pending_sectors").Value);
        root = Report("NVMe"); root["nvme_smart_health_information_log"]!["media_errors"] = 9007199254740992L;
        result = SmartReports.Parse(Disk(), new(root.ToJsonString()), Now); Assert.Null(Assert.Single(result.Readings, x => x.Metric == "nvme.media_errors").Value);
    }
    [Fact] public void IdentityAndSchemaAmbiguityAreRejected()
    {
        var disk = Disk(); disk.Serial = "OTHER"; Assert.Throws<InvalidDataException>(() => SmartReports.Parse(disk, new(Report().ToJsonString()), Now));
        disk = Disk(); disk.MappingConfidence = "ambiguous"; Assert.Throws<InvalidDataException>(() => SmartReports.Parse(disk, new(Report().ToJsonString()), Now));
        var root = Report(); root["json_format_version"]![0] = 2; Assert.Throws<InvalidDataException>(() => SmartReports.Parse(Disk(), new(root.ToJsonString()), Now));
        root = Report("UNSUPPORTED"); Assert.Throws<InvalidDataException>(() => SmartReports.Parse(Disk(), new(root.ToJsonString()), Now));
        root = Report(); root["ata_smart_attributes"]!["table"]!.AsArray().Add(root["ata_smart_attributes"]!["table"]![0]!.DeepClone());
        Assert.Throws<InvalidDataException>(() => SmartReports.Parse(Disk(), new(root.ToJsonString()), Now));
        var duplicate = Report().ToJsonString().Replace("\"model_name\":", "\"serial_number\":\"OTHER\",\"model_name\":");
        Assert.Throws<InvalidDataException>(() => SmartReports.Parse(Disk(), new(duplicate), Now));
    }
    [Fact] public void SemanticHashIgnoresFormattingAndUnstoredFieldsButChangesWithMeasurements()
    {
        var root = Report(); var first = SmartReports.Parse(Disk(), new(root.ToJsonString()), Now);
        root["private_output"] = "another secret";
        Assert.Equal(first.ReportHash, SmartReports.Parse(Disk(), new(root.ToJsonString(new() { WriteIndented = true })), Now).ReportHash);
        root["temperature"]!["current"] = 33;
        Assert.NotEqual(first.ReportHash, SmartReports.Parse(Disk(), new(root.ToJsonString()), Now).ReportHash);
    }
}
internal static class SmartReportFixtureExtensions
{
    public static JsonObject WithProtocol(this JsonObject root, string protocol) { root["device"]!["protocol"] = protocol; return root; }
}
