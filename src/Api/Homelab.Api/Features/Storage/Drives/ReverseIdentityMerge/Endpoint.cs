using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.IdentityHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.ReverseIdentityMerge;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.IdentityHistoryMerge>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/identity-history/{mergeId:guid}/reverse");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Reverse Identity Merge");
        Description(b =>
        {
            b.WithName("StorageDrivesReverseIdentityMerge");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.IdentityHistoryMerge>(200);
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
        var mergeId = request.MergeId;
        var input = request.Input;
        var ctx = HttpContext;
        var row = await db.IdentityMerges.FindAsync(mergeId);
        if (row is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Record why the histories should be split, in 1–4000 characters." }));
            return;
        }

        var graph = await Graph(db, row.ConnectionId);
        var members = graph.Group(id);
        if (row.ReversedUtc != null)
        {
            await Send.ResultAsync((members.Contains(row.FirstDiskId) || members.Contains(row.SecondDiskId)) && row.ReversalEvidence == input.Evidence ? Results.Ok(row) : Results.Conflict(new { error = "This merge was already reversed." }));
            return;
        }

        if (!members.Contains(row.FirstDiskId) || !members.Contains(row.SecondDiskId))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (graph.Token(members) != input.ReviewToken)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "The identity group changed. Reload before splitting it." }));
            return;
        }

        row.ReversedUtc = DateTime.UtcNow;
        row.ReversalEvidence = input.Evidence;
        row.ReversedBy = ctx.User.Identity!.Name!;
        db.Activity.Add(new() { Actor = row.ReversedBy, Action = "Identity history merge reversed", Detail = JsonSerializer.Serialize(row) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(row));
        return;
    }
}
