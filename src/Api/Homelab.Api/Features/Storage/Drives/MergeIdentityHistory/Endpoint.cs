using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.IdentityHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.MergeIdentityHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.IdentityHistoryMerge>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/identity-history/merge");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Merge Identity History");
        Description(b =>
        {
            b.WithName("StorageDrivesMergeIdentityHistory");
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
        var input = request.Input;
        var ctx = HttpContext;
        var previous = await db.IdentityMerges.SingleOrDefaultAsync(x => x.RequestId == input.RequestId);
        if (previous is not null)
        {
            await Send.ResultAsync(previous.FirstDiskId == id && previous.SecondDiskId == input.OtherDiskId && previous.SubmissionHash == SubmissionHash(input) ? Results.Ok(previous) : Results.Conflict(new { error = "This request ID was already used for different evidence." }));
            return;
        }

        var disk = await db.Disks.FindAsync(id);
        var other = await db.Disks.FindAsync(input.OtherDiskId);
        if (disk is null || other is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (id == other.Id || disk.ConnectionId != other.ConnectionId || input.RequestId == Guid.Empty || !input.ConfirmedSamePhysicalDrive || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000 || input.FirstIdentity != Confirmation(disk) || input.SecondIdentity != Confirmation(other))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Confirm both exact identities, the same physical drive assertion, and supporting evidence in 1–4000 characters." }));
            return;
        }

        var graph = await Graph(db, disk.ConnectionId);
        var first = graph.Group(id);
        var second = graph.Group(other.Id);
        var ids = first.Concat(second).ToArray();
        if (first.Contains(other.Id) || ids.Length > 64 || graph.Token(ids) != input.ReviewToken)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "The records or identity groups changed. Reload the review before merging." }));
            return;
        }

        var row = new IdentityHistoryMerge
        {
            ConnectionId = disk.ConnectionId,
            FirstDiskId = id,
            SecondDiskId = other.Id,
            RequestId = input.RequestId,
            SubmissionHash = SubmissionHash(input),
            Evidence = input.Evidence,
            Author = ctx.User.Identity!.Name!,
            RecordedUtc = DateTime.UtcNow
        };
        db.IdentityMerges.Add(row);
        db.Activity.Add(new() { Actor = row.Author, Action = "Identity histories manually merged", Detail = JsonSerializer.Serialize(row) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(row));
        return;
    }
}
