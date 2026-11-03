using Microsoft.EntityFrameworkCore;
using static Homelab.Api.IdentityHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.PreviewIdentityMerge;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/identity-history/review");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Preview Identity Merge");
        Description(b =>
        {
            b.WithName("StorageDrivesPreviewIdentityMerge");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var otherId = request.OtherId;
        var disk = await db.Disks.FindAsync(id);
        var other = await db.Disks.FindAsync(otherId);
        if (disk is null || other is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (id == otherId || disk.ConnectionId != other.ConnectionId)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose two distinct records from the same NAS." }));
            return;
        }

        var graph = await Graph(db, disk.ConnectionId);
        var first = graph.Group(id);
        var second = graph.Group(otherId);
        if (first.Contains(otherId))
        {
            await Send.ResultAsync(Results.Conflict(new { error = "These records already share an identity history group." }));
            return;
        }

        var ids = first.Concat(second).ToArray();
        if (ids.Length > 64)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "An identity history group supports at most 64 records." }));
            return;
        }

        await Send.ResultAsync(Results.Ok(new Response(graph.Token(ids), Confirmation(disk), Confirmation(other), graph.Disks.Where(x => ids.Contains(x.Id)).Select(x => new ResponseRecordsItem(x.Id, x.Serial, x.Model, x.Wwn, x.MappingConfidence, x.Present, x.Bay, x.BayVerified)), "Review all records before merging their histories. Original IDs and evidence remain intact. This does not resolve discovery ambiguity, combine counter rates, verify bays or authorize physical actions.")));
        return;
    }
}
