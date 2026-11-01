using Homelab.Api;
namespace Homelab.Tests;
public sealed class MaintenanceTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static Disk Disk => new() { Serial = "DRIVE-123", MappingConfidence = "verified" };
    private static MaintenanceInput Input => new(Guid.NewGuid(), Now.AddHours(-1), "documented", "inspection", "unknown", "Inspected connection", "Operator maintenance log", "DRIVE-123");
    [Theory]
    [InlineData("future")][InlineData("timezone")][InlineData("old")][InlineData("confidence")][InlineData("kind")][InlineData("outcome")]
    [InlineData("summary")][InlineData("evidence")][InlineData("identity")][InlineData("request")]
    public void InvalidOrMisattributedWorkIsRejected(string scenario)
    {
        var input = scenario switch {
            "future" => Input with { OccurredUtc = Now.AddDays(1) },
            "timezone" => Input with { OccurredUtc = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) },
            "old" => Input with { OccurredUtc = DateTime.UnixEpoch.AddDays(-1) },
            "confidence" => Input with { DateConfidence = "certain" },
            "kind" => Input with { Kind = "run-scrub" },
            "outcome" => Input with { Outcome = "healthy" },
            "summary" => Input with { Summary = null! },
            "evidence" => Input with { Evidence = new string('x',4001) },
            "identity" => Input with { ConfirmedIdentity = "OTHER" },
            _ => Input with { RequestId = Guid.Empty }
        };
        Assert.NotNull(MaintenanceHistory.Validate(Disk,input,Now));
    }
    [Fact] public void AbsentDriveCanKeepHistoryButAmbiguousIdentityCannotBeConfirmed()
    {
        var disk = Disk; disk.Present = false;
        Assert.Null(MaintenanceHistory.Validate(disk,Input,Now));
        disk.MappingConfidence = "ambiguous"; Assert.NotNull(MaintenanceHistory.Validate(disk,Input,Now));
        disk.MappingConfidence = "verified"; disk.Serial = ""; disk.Wwn = "5000000000000001";
        Assert.Null(MaintenanceHistory.Validate(disk,Input with { ConfirmedIdentity = disk.Wwn },Now));
    }
}
