using Homelab.Api;
using System.Text.Json.Nodes;
namespace Homelab.Tests;
public class NvmeEvidenceTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static Observation Sample(string metric, double value) => new() { Metric=metric, Unit=ObservationValidation.CanonicalUnit(metric)!, Value=value, Quality="observed", ObservedUtc=Now.AddMinutes(-1), Source="fixture", SourceKey=Guid.NewGuid().ToString(), SourceSchema="nvme-v2", ResetEpoch="one" };
    private static JsonObject Report() => JsonNode.Parse("""{"json_format_version":[1,0],"smartctl":{"version":[7,4],"exit_status":0},"device":{"protocol":"NVMe"},"serial_number":"NVME","model_name":"model","local_time":{"time_t":1793491200},"nvme_smart_health_information_log":{"media_errors":0,"critical_warning":8,"available_spare":97,"available_spare_threshold":10,"percentage_used":130}}""")!.AsObject();
    private static SmartReportPreview Parse(JsonObject root) => SmartReports.Parse(new Disk {Serial="NVME",Model="model"},new(root.ToJsonString(),"one"),Now);
    [Fact] public void ParserRetainsProtocolFieldsAndSeparatesEnduranceFromProbability() {
        var report = Parse(Report()); Assert.EndsWith("-v5",report.SourceSchema);
        Assert.Equal(130,report.Readings.Single(x=>x.Metric=="nvme.percentage_used").Value);
        Assert.Equal("% rated endurance",report.Readings.Single(x=>x.Metric=="nvme.percentage_used").Unit);
        Assert.Equal(97,report.Readings.Single(x=>x.Metric=="nvme.available_spare").Value);
        Assert.Contains(report.Warnings,x=>x.Contains("not a failure probability"));
    }
    [Theory][InlineData("nvme.critical_warning",256)][InlineData("nvme.percentage_used",256)][InlineData("nvme.available_spare",101)][InlineData("nvme.available_spare_threshold",101)][InlineData("nvme.percentage_used",1.5)][InlineData("nvme.critical_warning",-1)]
    public void OutOfRangeOrFractionalFieldsAreNotValidObservations(string metric,double value) => Assert.NotNull(ObservationValidation.Validate(Sample(metric,value),Now));
    [Fact] public void AbsentAndInvalidFieldsStayUnknown() {
        var root = Report(); root["nvme_smart_health_information_log"]!["critical_warning"] = "0x08";
        root["nvme_smart_health_information_log"]!["available_spare"] = 101;
        root["nvme_smart_health_information_log"]!.AsObject().Remove("percentage_used");
        var result = Parse(root);
        foreach(var metric in new[]{"nvme.critical_warning","nvme.available_spare","nvme.percentage_used"}) {
            var reading=result.Readings.Single(x=>x.Metric==metric); Assert.Null(reading.Value); Assert.Equal("unknown",reading.Quality);
        }
    }
    [Fact] public void ChangedFieldsChangeNewHashButPreserveLegacyReplayKey() {
        var root = Report(); var first=Parse(root); root["nvme_smart_health_information_log"]!["percentage_used"]=140;
        var next=Parse(root); Assert.NotEqual(first.ReportHash,next.ReportHash); Assert.Equal(first.LegacyReportHash,next.LegacyReportHash);
        Assert.NotEqual(first.ReportHash,first.LegacyReportHash);
    }
    [Theory][InlineData(8,"critical","read-only")][InlineData(2,"urgent","temperature")][InlineData(64,"urgent","unrecognized")]
    public void WarningMasksDescribeReportedConditionsWithoutInventingUnknownBits(int flags,string severity,string evidence) {
        var result=HealthRules.Evaluate(Sample("nvme.critical_warning",flags),null)!;
        Assert.Equal("nvme-warning-v1",result.Key); Assert.Equal(severity,result.Severity); Assert.Contains(evidence,result.Evidence);
    }
    [Theory][InlineData(99,false)][InlineData(100,true)][InlineData(255,true)]
    public void EnduranceBoundaryIsAReviewNotFailurePrediction(int used,bool review) {
        var result=HealthRules.Evaluate(Sample("nvme.percentage_used",used),null);
        Assert.Equal(review,result is not null);
        if(result is not null) { Assert.Equal("review",result.Severity); Assert.Contains("does not by itself establish failure",result.NextStep); }
    }
    [Fact] public void UnknownOrUnverifiedWarningsCannotOpenFindings() {
        var value=Sample("nvme.critical_warning",8); value.Quality="unverified"; Assert.Null(HealthRules.Evaluate(value,null));
        value.Quality="unknown"; value.Value=null; Assert.Null(HealthRules.Evaluate(value,null));
    }
    [Fact] public void TwoLaterFreshZeroMasksCanResolveButResetCannot() {
        var finding=new Finding { Rule="nvme-warning-v1", EvidenceObservedUtc=Now.AddMinutes(-10), EvidenceSource="fixture", EvidenceSchema="nvme-v2", EvidenceEpoch="one", EvidenceUnit="bitmask" };
        var previous=Sample("nvme.critical_warning",0); previous.ObservedUtc=Now.AddMinutes(-2);
        var current=Sample("nvme.critical_warning",0);
        Assert.True(FindingLifecycle.CanResolvePresence(finding,current,previous,Now,TimeSpan.FromHours(1)));
        current.ResetEpoch="two"; Assert.False(FindingLifecycle.CanResolvePresence(finding,current,previous,Now,TimeSpan.FromHours(1)));
        current.ResetEpoch="one"; Assert.False(FindingLifecycle.CanResolvePresence(finding,current,previous,Now.AddDays(1),TimeSpan.FromHours(1)));
    }
}
