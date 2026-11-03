using Microsoft.EntityFrameworkCore;
using static Homelab.Api.IdentityHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetIdentityHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/identity-history");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Identity History");
        Description(b =>
        {
            b.WithName("StorageDrivesGetIdentityHistory");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var before = request.Before;
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (before is <= 0)
        {
            await Send.ResultAsync(Results.BadRequest());
            return;
        }

        var graph = await Graph(db, disk.ConnectionId);
        var members = graph.Group(id);
        var observations = await db.Observations.AsNoTracking().Where(x => x.DiskId != null && members.Contains(x.DiskId.Value) && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(51).ToArrayAsync();
        await Send.ResultAsync(Results.Ok(new Response(graph.Token(members), graph.Disks.Where(x => members.Contains(x.Id)).Select(x => new ResponseMembersItem(x.Id, x.Serial, x.Model, x.Wwn, x.MappingConfidence, x.Present, x.Bay, x.BayVerified, Confirmation(x))), graph.Disks.Where(x => !members.Contains(x.Id)).Select(x => new ResponseCandidatesItem(x.Id, x.Serial, x.Model, x.Wwn, Confirmation(x))), graph.Links.Where(x => members.Contains(x.FirstDiskId) || members.Contains(x.SecondDiskId)).OrderByDescending(x => x.RecordedUtc), observations.Take(50), observations.Length > 50 ? (long? )observations[49].Id : null, await db.Findings.AsNoTracking().Where(x => x.DiskId != null && members.Contains(x.DiskId.Value)).OrderByDescending(x => x.LastSeenUtc).Take(51).ToArrayAsync())));
        return;
    }
}
