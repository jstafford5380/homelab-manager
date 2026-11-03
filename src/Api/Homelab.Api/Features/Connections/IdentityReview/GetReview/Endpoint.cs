using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DiscoveryAssignments;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.IdentityReview.GetReview;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/identity-review");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Review");
        Description(b =>
        {
            b.WithName("ConnectionsIdentityReviewGetReview");
            b.WithTags("Connections");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var nas = await db.Connections.FindAsync(id);
        if (nas is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var snapshot = Read(nas);
        var disks = await db.Disks.AsNoTracking().Where(x => x.ConnectionId == id).ToArrayAsync();
        var assignments = await db.DiscoveryAssignments.AsNoTracking().Where(x => x.ConnectionId == id).OrderByDescending(x => x.RecordedUtc).ToArrayAsync();
        var now = DateTime.UtcNow;
        await Send.ResultAsync(Results.Ok(new Response(Usable(nas, snapshot, now), snapshot?.FetchedUtc, snapshot is null ? null : Token(nas, snapshot, disks, assignments), (snapshot?.Rows ?? []).GroupBy(x => x.Key).Select(g => new ResponseGroupsItem(IdentityCandidate.NormalizeWwn(g.First().Wwn), g.First().Serial, g.First().Model, g.Select(x => x.Alias).Distinct(), Compatible(g) && AliasConsistent(g, snapshot!.Rows), disks.Where(d => IdentityCandidate.NormalizeWwn(d.Wwn) == IdentityCandidate.NormalizeWwn(g.First().Wwn) && IdentityCandidate.NormalizeWwn(d.Wwn) != null && d.Serial.Trim() == g.First().Serial.Trim() && d.Model.Trim() == g.First().Model.Trim()).Select(d => new ResponseGroupsItemRecordsItem(d.Id, d.Serial, d.Model, d.MappingConfidence, d.Lifecycle)))), assignments.Where(x => x.RevokedUtc == null && x.ValidUntilUtc > now).Concat(assignments.Where(x => x.RevokedUtc != null || x.ValidUntilUtc <= now).Take(50)), assignments.Count(x => x.RevokedUtc != null || x.ValidUntilUtc <= now) > 50)));
        return;
    }
}
