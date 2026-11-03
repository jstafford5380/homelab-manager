using Homelab.Api;
namespace Homelab.Tests;
public class RecoveryTests
{
    private readonly DateTime now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData("capacity", 4001)]
    [InlineData("requirements", 10001)]
    [InlineData("capacity", -1)]
    [InlineData("requirements", -1)]
    public void PlanningContextRejectsNullAndOversizedValues(string field, int length)
    {
        var policy = new RecoveryPolicy { Asset = "photos", Destination = "offsite", Scope = "photos" };
        var value = length < 0 ? null! : new string('x', length);
        if (field == "capacity") policy.DestinationCapacity = value;
        else policy.RestoreRequirements = value;
        Assert.NotNull(RecoveryRules.ValidatePolicy(policy));
    }
    [Fact]
    public void PlanningNotesDoNotEstablishRecoveryReadiness()
    {
        var policy = Policy();
        policy.DestinationCapacity = "10 TiB available";
        policy.RestoreRequirements = "Configuration and application dependencies recorded";
        var result = RecoveryEvaluator.Assess(policy, [], [], now);
        Assert.Equal("recovery gap", result.Protection);
        Assert.Equal("unverified", result.RecoveryPoint);
        Assert.Equal("not tested", result.Restore);
    }
    private RecoveryPolicy Policy() => new() { Scope = "photos", RpoHours = 24, RtoHours = 4, Independence = "verified" };
    private BackupEvidence Run(RecoveryPolicy p) => new() { PolicyRevision = p.Revision, Evidence = "Destination manifest", Outcome = "success", DestinationConfirmed = true, AttemptedUtc = now.AddMinutes(-1), RecoveryPointUtc = now.AddHours(-1), ReviewByUtc = now.AddDays(1) };
    private RestoreExercise Exercise(RecoveryPolicy p) => new() { PolicyRevision = p.Revision, Scope = p.Scope, Evidence = "Restore log", Checks = "Verified contents", RecoveryPointUtc = now.AddHours(-2), TestedUtc = now.AddHours(-1), ReviewByUtc = now.AddDays(30), Result = "passed", DurationMinutes = 60 };
    [Fact] public void LocalSnapshotNeverBecomesIndependentProtection() { var p = Policy(); p.Mechanism = "local snapshot"; Assert.Equal("source-pool only", RecoveryEvaluator.Assess(p, [Run(p)], [Exercise(p)], now).Protection); }
    [Fact] public void SuccessfulCopyWithoutDestinationEvidenceIsUnverified() { var p = Policy(); var run = Run(p); run.DestinationConfirmed = false; Assert.Equal("unverified", RecoveryEvaluator.Assess(p, [run], [], now).RecoveryPoint); }
    [Fact] public void RpoUsesRepresentedPointRatherThanRecentFinishTime() { var p = Policy(); var run = Run(p); run.RecoveryPointUtc = now.AddDays(-2); Assert.Equal("RPO exceeded", RecoveryEvaluator.Assess(p, [run], [], now).RecoveryPoint); }
    [Fact] public void CopySuccessWithoutRestoreKeepsRtoAsTarget() { var p = Policy(); var result = RecoveryEvaluator.Assess(p, [Run(p)], [], now); Assert.Equal("restore unverified", result.Protection); Assert.Equal("target only", result.Rto); }
    [Fact] public void ExpiredEvidenceCannotEstablishRecoverability() { var p = Policy(); var run = Run(p); run.ReviewByUtc = now.AddSeconds(-1); var exercise = Exercise(p); exercise.ReviewByUtc = now.AddSeconds(-1); var result = RecoveryEvaluator.Assess(p, [run], [exercise], now); Assert.Equal("unverified", result.RecoveryPoint); Assert.Equal("evidence expired", result.Restore); }
    [Fact] public void NewerFailedRestoreOverridesEarlierPass() { var p = Policy(); var failed = Exercise(p); failed.TestedUtc = now; failed.Result = "failed"; Assert.Equal("failed", RecoveryEvaluator.Assess(p, [Run(p)], [Exercise(p), failed], now).Restore); }
    [Fact] public void UnrelatedScopeOrOldPolicyRevisionCannotProveRestore() { var p = Policy(); var exercise = Exercise(p); exercise.Scope = "different"; Assert.Equal("not tested", RecoveryEvaluator.Assess(p, [Run(p)], [exercise], now).Restore); exercise.Scope = p.Scope; exercise.PolicyRevision = Guid.NewGuid(); Assert.Equal("not tested", RecoveryEvaluator.Assess(p, [Run(p)], [exercise], now).Restore); }
    [Fact] public void MeasuredRestoreCanExceedRtoWithoutClaimingTargetMet() { var p = Policy(); var exercise = Exercise(p); exercise.DurationMinutes = 300; Assert.Equal("measured beyond target", RecoveryEvaluator.Assess(p, [Run(p)], [exercise], now).Rto); }
    [Fact] public void ScopedDemonstrationRequiresAllIndependentEvidence() { var p = Policy(); Assert.Equal("scoped recovery demonstrated", RecoveryEvaluator.Assess(p, [Run(p)], [Exercise(p)], now).Protection); }
    [Fact] public void FutureBackupEvidenceCannotEstablishRecoveryPoint() {
        var p = Policy(); var run = Run(p); run.AttemptedUtc = now.AddHours(2); run.RecoveryPointUtc = now.AddHours(1);
        var assessment = RecoveryEvaluator.Assess(p, [run], [Exercise(p)], now);
        Assert.Equal("unverified", assessment.RecoveryPoint); Assert.Null(assessment.RepresentedPointUtc); Assert.Equal("unknown", assessment.LatestAttempt);
    }
    [Fact] public void MalformedLatestRestoreDoesNotFallBackToEarlierPassedExercise() {
        var p = Policy(); var malformed = Exercise(p); malformed.TestedUtc = now; malformed.DurationMinutes = 0;
        var assessment = RecoveryEvaluator.Assess(p, [Run(p)], [Exercise(p), malformed], now);
        Assert.Equal("unverified evidence", assessment.Restore); Assert.Equal("target only", assessment.Rto); Assert.Equal("restore unverified", assessment.Protection);
    }
    [Fact] public void MissingHistoricalEvidenceCannotProveReadiness() {
        var p = Policy(); var run = Run(p); run.Evidence = ""; var exercise = Exercise(p); exercise.Checks = null!;
        var assessment = RecoveryEvaluator.Assess(p, [run], [exercise], now);
        Assert.Equal("unverified", assessment.RecoveryPoint); Assert.Equal("unverified evidence", assessment.Restore);
    }
    [Fact] public void InvalidHistoricalTargetsDoNotOverflowOrClaimMet() {
        var p = Policy(); p.RpoHours = int.MaxValue; p.RtoHours = int.MaxValue;
        var assessment = RecoveryEvaluator.Assess(p, [Run(p)], [Exercise(p)], now);
        Assert.NotEqual("within RPO", assessment.RecoveryPoint); Assert.Equal("target only", assessment.Rto);
    }
}
