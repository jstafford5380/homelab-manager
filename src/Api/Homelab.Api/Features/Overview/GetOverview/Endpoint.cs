using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Overview.GetOverview;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/overview");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Overview");
        Description(b =>
        {
            b.WithName("GetOverview");
            b.WithTags("Overview");
            b.Produces<Response>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var recoveryPolicies = await db.Recovery.ToListAsync();
        var backupEvidence = await db.BackupEvidence.ToListAsync();
        var restoreExercises = await db.RestoreExercises.ToListAsync();
        await Send.ResultAsync(Results.Ok(new Response(await db.Connections.Select(x => new ResponseConnectionsItem(x.Id, x.Name, x.ApiUri, x.ConnectHost, x.ConnectPort, x.Version, x.Status, x.LastSuccessUtc, x.PollSeconds, x.Enabled)).ToListAsync(), (await db.Capabilities.AsNoTracking().ToListAsync()).Select(x => new ResponseCapabilitiesItem(x.ConnectionId, x.Name, x.EffectiveStatus(DateTime.UtcNow), x.LastSuccessUtc)), await db.Settings.SingleOrDefaultAsync() ?? new OperatingSettings(), await db.Disks.ToListAsync(), await db.Findings.ToListAsync(), await db.Plans.ToListAsync(), recoveryPolicies, await ScanOverview.ReadAsync(db, DateTime.UtcNow, ct), await CapacityOutlook.ReadAsync(db, DateTime.UtcNow, ct), recoveryPolicies.Select(p => new ResponseRecoveryAssessmentsItem(p.Id, RecoveryEvaluator.Assess(p, backupEvidence.Where(r => r.PolicyId == p.Id), restoreExercises.Where(r => r.PolicyId == p.Id), DateTime.UtcNow))), await db.Activity.OrderByDescending(x => x.Id).Take(100).ToListAsync())));
        return;
    }
}
