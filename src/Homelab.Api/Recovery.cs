using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public sealed record RecoveryAssessment(string Protection, string RecoveryPoint, string Restore, string Rto, DateTime? RepresentedPointUtc, string LatestAttempt);
public static class RecoveryEvaluator
{
    public static RecoveryAssessment Assess(RecoveryPolicy policy, IEnumerable<BackupEvidence> runs, IEnumerable<RestoreExercise> exercises, DateTime now)
    {
        var current = runs.Where(r => r.PolicyRevision == policy.Revision).OrderByDescending(r => r.AttemptedUtc).ThenByDescending(r => r.RecordedUtc).ToArray();
        var successful = current.Where(r => r.Outcome == "success" && r.DestinationConfirmed && r.RecoveryPointUtc != null && r.ReviewByUtc > now && RecoveryValidation.Run(r, now, submission: false) is null).OrderByDescending(r => r.RecoveryPointUtc).FirstOrDefault();
        var point = successful?.RecoveryPointUtc;
        var pointStatus = point is null ? "unverified" : policy.RpoHours is < 1 or > 8760 ? "target unverified" : now - point > TimeSpan.FromHours(policy.RpoHours) ? "RPO exceeded" : "within RPO";
        var restore = exercises.Where(r => r.PolicyRevision == policy.Revision && r.Scope == policy.Scope).OrderByDescending(r => r.TestedUtc).ThenByDescending(r => r.RecordedUtc).FirstOrDefault();
        var restoreStatus = restore is null ? "not tested" : RecoveryValidation.Exercise(restore, now, submission: false) is not null ? "unverified evidence" : restore.ReviewByUtc <= now ? "evidence expired" : restore.Result;
        var rto = restoreStatus == "passed" && policy.RtoHours is >= 1 and <= 8760 ? restore!.DurationMinutes <= policy.RtoHours * 60 ? "demonstrated within target" : "measured beyond target" : "target only";
        var protection = policy.Mechanism == "local snapshot" ? "source-pool only" : policy.Independence != "verified" ? "independence unverified" : pointStatus != "within RPO" ? "recovery gap" : restoreStatus != "passed" ? "restore unverified" : "scoped recovery demonstrated";
        return new(protection, pointStatus, restoreStatus, rto, point, current.FirstOrDefault() is {} latest && RecoveryValidation.Run(latest, now, submission: false) is null ? latest.Outcome : "unknown");
    }
}
public static class RecoveryEndpoints
{
    public static void MapRecovery(this RouteGroupBuilder api)
    {
        api.MapRecoveryHistory();
        api.MapGet("/recovery", async (HomelabDb db) => {
            var policies = await db.Recovery.ToListAsync(); var runs = await db.BackupEvidence.ToListAsync(); var exercises = await db.RestoreExercises.ToListAsync();
            return policies.Select(p => new { policy = p, assessment = RecoveryEvaluator.Assess(p, runs.Where(r => r.PolicyId == p.Id), exercises.Where(r => r.PolicyId == p.Id), DateTime.UtcNow), runs = runs.Where(r => r.PolicyId == p.Id).OrderByDescending(r => r.AttemptedUtc).Take(100), exercises = exercises.Where(r => r.PolicyId == p.Id).OrderByDescending(r => r.TestedUtc).Take(100) });
        });
        api.MapPost("/recovery", async (RecoveryPolicy input, HomelabDb db, HttpContext ctx) => {
            if (ValidatePolicy(input) is {} error) return Results.BadRequest(new { error });
            input.Id = Guid.NewGuid(); input.Revision = Guid.NewGuid(); input.Owner = ctx.User.Identity!.Name!;
            input.RestoreResult = "unverified"; input.RestoreTestUtc = null; input.RecoveryPointUtc = null;
            db.Recovery.Add(input); db.Activity.Add(new() { Actor = input.Owner, Action = "Protection policy created", Detail = input.Asset }); await db.SaveChangesAsync(); return Results.Ok(input);
        });
        api.MapPut("/recovery/{id:guid}", async (Guid id, RecoveryPolicy input, HomelabDb db, HttpContext ctx) => {
            var policy = await db.Recovery.FindAsync(id); if (policy is null) return Results.NotFound();
            if (input.Revision != policy.Revision) return StalePolicy();
            if (ValidatePolicy(input) is {} error) return Results.BadRequest(new { error });
            input.Id = id; input.Revision = Guid.NewGuid(); input.Owner = ctx.User.Identity!.Name!;
            input.RestoreResult = "unverified"; input.RestoreTestUtc = null; input.RecoveryPointUtc = null;
            db.Entry(policy).CurrentValues.SetValues(input); db.Activity.Add(new() { Actor = input.Owner, Action = "Protection policy revised; evidence requires revalidation", Detail = input.Asset }); await db.SaveChangesAsync(); return Results.Ok(policy);
        });
        api.MapPost("/recovery/{id:guid}/runs", async (Guid id, BackupEvidence input, HomelabDb db, HttpContext ctx) => {
            var policy = await db.Recovery.FindAsync(id); if (policy is null) return Results.NotFound();
            if (input.PolicyRevision != policy.Revision) return StalePolicy();
            var now = DateTime.UtcNow;
            if (RecoveryValidation.Run(input, now) is {} error) return Results.BadRequest(new { error });
            input.Id = Guid.NewGuid(); input.PolicyId = id; input.PolicyRevision = policy.Revision; input.Author = ctx.User.Identity!.Name!; input.RecordedUtc = now;
            db.BackupEvidence.Add(input); db.Activity.Add(new() { Actor = input.Author, Action = "Backup evidence recorded", Detail = policy.Asset }); await db.SaveChangesAsync(); return Results.Ok(input);
        });
        api.MapPost("/recovery/{id:guid}/exercises", async (Guid id, RestoreExercise input, HomelabDb db, HttpContext ctx) => {
            var policy = await db.Recovery.FindAsync(id); if (policy is null) return Results.NotFound();
            if (input.PolicyRevision != policy.Revision) return StalePolicy();
            var now = DateTime.UtcNow;
            if (RecoveryValidation.Exercise(input, now) is {} error) return Results.BadRequest(new { error });
            input.Id = Guid.NewGuid(); input.PolicyId = id; input.PolicyRevision = policy.Revision; input.Author = ctx.User.Identity!.Name!; input.RecordedUtc = now;
            db.RestoreExercises.Add(input); db.Activity.Add(new() { Actor = input.Author, Action = "Restore exercise recorded", Detail = policy.Asset }); await db.SaveChangesAsync(); return Results.Ok(input);
        });
    }
    private static IResult StalePolicy() => Results.Conflict(new { error = "This protection policy changed since you opened it. Your draft has not been saved. Close this form and reopen the current policy to review its scope before submitting again." });
    public static string? ValidatePolicy(RecoveryPolicy input)
    {
        if (string.IsNullOrWhiteSpace(input.Asset) || string.IsNullOrWhiteSpace(input.Destination) || string.IsNullOrWhiteSpace(input.Scope) || input.RpoHours is < 1 or > 8760 || input.RtoHours is < 1 or > 8760 || input.Mechanism is not ("local snapshot" or "replication" or "independent backup" or "offline/off-site copy") || input.Independence is not ("unverified" or "shared" or "verified"))
            return "Enter asset, destination, scope, valid objectives, mechanism and independence.";
        foreach (var (label, value, limit) in new (string, string?, int)[] {
            ("Asset", input.Asset, 200), ("Destination", input.Destination, 2000), ("Scope", input.Scope, 10000),
            ("Exclusions", input.Exclusions, 10000), ("Retention", input.Retention, 2000), ("Schedule", input.Schedule, 2000),
            ("Destination capacity", input.DestinationCapacity, 4000), ("Restore requirements", input.RestoreRequirements, 10000),
            ("Failure domains", input.FailureDomains, 10000), ("Runbook", input.Runbook, 50000),
            ("Key custody reference", input.KeyCustodyReference, 2000), ("Evidence", input.Evidence, 10000), ("Importance", input.Importance, 200) })
            if (value is null || value.Length > limit) return $"{label} must be text with at most {limit} characters; leave optional fields empty if unknown.";
        return null;
    }
}
