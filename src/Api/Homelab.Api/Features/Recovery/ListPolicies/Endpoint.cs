using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Recovery.ListPolicies;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, System.Collections.Generic.IEnumerable<ResponseItem>>
{
    public override void Configure()
    {
        Get("/api/v1/recovery");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "List Policies");
        Description(b =>
        {
            b.WithName("RecoveryListPolicies");
            b.WithTags("Recovery");
            b.Produces<System.Collections.Generic.IEnumerable<ResponseItem>>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var policies = await db.Recovery.ToListAsync();
        var runs = await db.BackupEvidence.ToListAsync();
        var exercises = await db.RestoreExercises.ToListAsync();
        await Send.ResultAsync(Results.Ok(policies.Select(p => new ResponseItem(p, RecoveryEvaluator.Assess(p, runs.Where(r => r.PolicyId == p.Id), exercises.Where(r => r.PolicyId == p.Id), DateTime.UtcNow), runs.Where(r => r.PolicyId == p.Id).OrderByDescending(r => r.AttemptedUtc).Take(100), exercises.Where(r => r.PolicyId == p.Id).OrderByDescending(r => r.TestedUtc).Take(100)))));
        return;
    }
}
