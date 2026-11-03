using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class RecoveryEvaluator
{
    public static RecoveryAssessment Assess(RecoveryPolicy policy, IEnumerable<BackupEvidence> runs, IEnumerable<RestoreExercise> exercises, DateTime now)
    {
        var current = runs.Where(r => r.PolicyRevision == policy.Revision).OrderByDescending(r => r.AttemptedUtc).ThenByDescending(r => r.RecordedUtc).ToArray();
        var successful = current.Where(r => r.Outcome == "success" && r.DestinationConfirmed && r.RecoveryPointUtc != null && r.ReviewByUtc > now && RecoveryValidation.Run(r, now, submission: false)is null).OrderByDescending(r => r.RecoveryPointUtc).FirstOrDefault();
        var point = successful?.RecoveryPointUtc;
        var pointStatus = point is null ? "unverified" : policy.RpoHours is < 1 or > 8760 ? "target unverified" : now - point > TimeSpan.FromHours(policy.RpoHours) ? "RPO exceeded" : "within RPO";
        var restore = exercises.Where(r => r.PolicyRevision == policy.Revision && r.Scope == policy.Scope).OrderByDescending(r => r.TestedUtc).ThenByDescending(r => r.RecordedUtc).FirstOrDefault();
        var restoreStatus = restore is null ? "not tested" : RecoveryValidation.Exercise(restore, now, submission: false)is not null ? "unverified evidence" : restore.ReviewByUtc <= now ? "evidence expired" : restore.Result;
        var rto = restoreStatus == "passed" && policy.RtoHours is >= 1 and <= 8760 ? restore!.DurationMinutes <= policy.RtoHours * 60 ? "demonstrated within target" : "measured beyond target" : "target only";
        var protection = policy.Mechanism == "local snapshot" ? "source-pool only" : policy.Independence != "verified" ? "independence unverified" : pointStatus != "within RPO" ? "recovery gap" : restoreStatus != "passed" ? "restore unverified" : "scoped recovery demonstrated";
        return new(protection, pointStatus, restoreStatus, rto, point, current.FirstOrDefault()is { } latest && RecoveryValidation.Run(latest, now, submission: false)is null ? latest.Outcome : "unknown");
    }
}
