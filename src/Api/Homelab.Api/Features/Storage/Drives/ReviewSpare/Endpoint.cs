using Microsoft.EntityFrameworkCore;
using static Homelab.Api.SpareCompatibility;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.ReviewSpare;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.SpareCompatibilityReview>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/spare-reviews");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Review Spare");
        Description(b =>
        {
            b.WithName("StorageDrivesReviewSpare");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.SpareCompatibilityReview>(200);
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
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var existing = await db.SpareReviews.SingleOrDefaultAsync(x => x.DiskId == id && x.RequestId == input.RequestId);
        if (existing is not null)
        {
            await Send.ResultAsync(SameSubmission(existing, input) ? Results.Ok(existing) : Results.Conflict(new { error = "This request was already saved with different content. Refresh the review history." }));
            return;
        }

        var asset = await db.Assets.FindAsync(input.AssetId);
        if (asset is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (asset.UpdatedUtc != input.AssetUpdatedUtc)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "Spare inventory changed. Close this form and refresh planning before reviewing it again." }));
            return;
        }

        var now = DateTime.UtcNow;
        if (Validate(disk, asset, input, now)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        var row = new SpareCompatibilityReview
        {
            DiskId = id,
            AssetId = asset.Id,
            RequestId = input.RequestId,
            AssetUpdatedUtc = asset.UpdatedUtc,
            ConfirmedIdentity = input.ConfirmedIdentity,
            DiskModel = disk.Model,
            AssetName = asset.Name,
            Verdict = input.Verdict,
            Evidence = input.Evidence,
            ValidUntilUtc = input.ValidUntilUtc,
            RecordedUtc = now,
            Author = ctx.User.Identity!.Name!
        };
        db.SpareReviews.Add(row);
        db.Activity.Add(new() { Actor = row.Author, Action = "Spare compatibility reviewed", Detail = $"Drive {id}; spare {asset.Id}; {row.Verdict}. Manual assessment only; no reservation or NAS action." });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(row));
        return;
    }
}
