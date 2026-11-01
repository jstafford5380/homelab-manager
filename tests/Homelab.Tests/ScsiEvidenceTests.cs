using System.Text.Json.Nodes;
using Homelab.Api;
namespace Homelab.Tests;

public sealed class ScsiEvidenceTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static JsonObject Report() => new() {
        ["json_format_version"] = new JsonArray(1,0), ["smartctl"] = new JsonObject { ["version"] = new JsonArray(7,4), ["exit_status"] = 0 },
        ["device"] = new JsonObject { ["protocol"] = "SCSI" }, ["serial_number"] = "SERIAL", ["scsi_model_name"] = "VENDOR MODEL",
        ["local_time"] = new JsonObject { ["time_t"] = new DateTimeOffset(Now.AddMinutes(-1)).ToUnixTimeSeconds() },
        ["scsi_grown_defect_list"] = 12
    };
    private static SmartReportPreview Parse(JsonObject root) => SmartReports.Parse(new Disk { Serial="SERIAL", Model="VENDOR MODEL" }, new(root.ToJsonString()), Now);
    private static Observation Sample(double value, int minutes = -1) => new() {
        Metric="scsi.grown_defects", Value=value, Unit="defects", Quality="observed", ObservedUtc=Now.AddMinutes(minutes),
        Source="fixture", SourceKey=Guid.NewGuid().ToString(), SourceSchema="scsi-v1", ResetEpoch="one"
    };
    [Fact] public void CuratedScsiIdentityAndDefectElementsRemainProtocolSpecific()
    {
        var report=Parse(Report()); Assert.Equal("SCSI",report.Protocol); Assert.EndsWith("-scsi-tool-7.4-v3",report.SourceSchema);
        var reading=Assert.Single(report.Readings,x=>x.Metric=="scsi.grown_defects");
        Assert.Equal(12,reading.Value); Assert.Equal("defects",reading.Unit); Assert.Equal("observed",reading.Quality);
        Assert.DoesNotContain(report.Readings,x=>x.Metric.StartsWith("ata.")||x.Metric.StartsWith("nvme."));
        Assert.Contains(report.Warnings,x=>x.Contains("not ATA reallocated sectors"));
    }
    [Theory] [InlineData("null")] [InlineData("-1")] [InlineData("1.5")] [InlineData("\"12\"")] [InlineData("9007199254740992")]
    public void InvalidCountsRemainUnknown(string raw)
    {
        var root=Report(); root["scsi_grown_defect_list"]=JsonNode.Parse(raw);
        var reading=Assert.Single(Parse(root).Readings,x=>x.Metric=="scsi.grown_defects");
        Assert.Null(reading.Value); Assert.Equal("unknown",reading.Quality);
    }
    [Fact] public void OtherFieldsCannotStandInForMissingDefectCountOrScsiModel()
    {
        var root=Report(); root.Remove("scsi_grown_defect_list");
        root["nvme_smart_health_information_log"]=new JsonObject { ["media_errors"]=12 };
        Assert.Null(Assert.Single(Parse(root).Readings,x=>x.Metric=="scsi.grown_defects").Value);
        root["model_name"]="VENDOR MODEL"; root.Remove("scsi_model_name");
        Assert.Throws<InvalidDataException>(()=>Parse(root));
    }
    [Fact] public void UndatedOrCommandErrorCountsAreUnverified()
    {
        var root=Report(); root.Remove("local_time");
        Assert.Equal("unverified",Assert.Single(Parse(root).Readings,x=>x.Metric=="scsi.grown_defects").Quality);
        root=Report(); root["smartctl"]!["exit_status"]=2;
        Assert.Equal("unverified",Assert.Single(Parse(root).Readings,x=>x.Metric=="scsi.grown_defects").Quality);
    }
    [Theory] [InlineData(0)] [InlineData(12)] [InlineData(9007199254740991d)]
    public void ExactWholeDefectCountsValidate(double value) => Assert.Null(ObservationValidation.Validate(Sample(value),Now));
    [Theory] [InlineData(-1)] [InlineData(1.5)] [InlineData(9007199254740992d)]
    public void InvalidManualCountsFail(double value) => Assert.NotNull(ObservationValidation.Validate(Sample(value),Now));
    [Fact] public void OnlyComparableIncreasePromptsReview()
    {
        var before=Sample(12,-10); var current=Sample(13);
        var rule=HealthRules.Evaluate(current,before)!; Assert.Equal("urgent",rule.Severity); Assert.Contains("not ATA sectors",rule.NextStep);
        Assert.Null(HealthRules.Evaluate(before,null));
        Assert.Null(HealthRules.Evaluate(Sample(12),before)); Assert.Null(HealthRules.Evaluate(Sample(0),before));
        current.ResetEpoch=""; Assert.Null(HealthRules.Evaluate(current,before));
        current.ResetEpoch="two"; Assert.Null(HealthRules.Evaluate(current,before));
        current.ResetEpoch="one"; current.SourceSchema="other"; Assert.Null(HealthRules.Evaluate(current,before));
        current.SourceSchema="scsi-v1"; current.Quality="unverified"; Assert.Null(HealthRules.Evaluate(current,before));
    }
    [Fact] public void CuratedHashChangesWithDefectsButIgnoresUnknownFields()
    {
        var root=Report(); var first=Parse(root); root["secret"]="not retained";
        Assert.Equal(first.ReportHash,Parse(root).ReportHash);
        root["scsi_grown_defect_list"]=13; Assert.NotEqual(first.ReportHash,Parse(root).ReportHash);
    }
    [Theory] [InlineData("read")] [InlineData("write")] [InlineData("verify")]
    public void UncorrectedOperationsRemainSeparateAndComparable(string operation)
    {
        var root=Report();
        root["scsi_error_counter_log"]=new JsonObject { [operation]=new JsonObject { ["total_uncorrected_errors"]=5, ["total_errors_corrected"]=999 } };
        var result=Parse(root); var metric="scsi."+operation+"_uncorrected_errors";
        var reading=Assert.Single(result.Readings,x=>x.Metric==metric);
        Assert.Equal(5,reading.Value); Assert.Equal("errors",reading.Unit);
        Assert.All(result.Readings.Where(x=>x.Metric.EndsWith("_uncorrected_errors")&&x.Metric!=metric),x=>Assert.Null(x.Value));
        var before=Sample(4,-10); before.Metric=metric; before.Unit="errors";
        var current=Sample(5); current.Metric=metric; current.Unit="errors";
        Assert.Null(ObservationValidation.Validate(current,Now));
        Assert.Equal("scsi-uncorrected-increase-v1:"+metric,HealthRules.Evaluate(current,before)!.Key);
        Assert.Null(HealthRules.Evaluate(current,null));
        current.Value=4; Assert.Null(HealthRules.Evaluate(current,before));
        current.Value=0; Assert.Null(HealthRules.Evaluate(current,before));
        current.Value=5; current.ResetEpoch="changed"; Assert.Null(HealthRules.Evaluate(current,before));
    }
    [Theory] [InlineData("null")] [InlineData("-1")] [InlineData("0.5")] [InlineData("\"5\"")] [InlineData("9007199254740992")]
    public void MalformedErrorCountsCannotUseCorrectedErrorFallback(string value)
    {
        var root=Report(); root["scsi_error_counter_log"]=new JsonObject();
        foreach(var operation in new[]{"read","write","verify"})
            root["scsi_error_counter_log"]![operation]=new JsonObject { ["total_uncorrected_errors"]=JsonNode.Parse(value), ["total_errors_corrected"]=100 };
        Assert.All(Parse(root).Readings.Where(x=>x.Metric.EndsWith("_uncorrected_errors")),x=>{Assert.Null(x.Value); Assert.Equal("unknown",x.Quality);});
    }
    [Fact] public void LegacyScsiFingerprintExcludesNewCounters()
    {
        var root=Report(); var first=Parse(root);
        root["scsi_error_counter_log"]=new JsonObject { ["read"]=new JsonObject { ["total_uncorrected_errors"]=5 } };
        var second=Parse(root); Assert.NotEqual(first.ReportHash,second.ReportHash);
        Assert.NotNull(second.ScsiV1ReportHash); Assert.Equal(first.ScsiV1ReportHash,second.ScsiV1ReportHash);
        root["smartctl"]!["exit_status"]=2;
        Assert.Equal("unverified",Assert.Single(Parse(root).Readings,x=>x.Metric=="scsi.read_uncorrected_errors").Quality);
    }
}
