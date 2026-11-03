using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DrivePaths;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.RecordHardwarePath;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DrivePathReview>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/hardware-paths");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Record Hardware Path");
        Description(b =>
        {
            b.WithName("StorageDrivesRecordHardwarePath");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.DrivePathReview>(200);
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

        var existing = await db.DrivePaths.SingleOrDefaultAsync(x => x.DiskId == id && x.RequestId == input.RequestId);
        if (existing is not null)
        {
            await Send.ResultAsync(Same(existing, input) ? Results.Ok(existing) : Results.Conflict(new { error = "This submission ID was already used with different evidence." }));
            return;
        }

        var asset = await db.Assets.FindAsync(input.AssetId);
        if (asset is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var now = DateTime.UtcNow;
        if (Validate(disk, asset, input, now)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        if (asset.UpdatedUtc != input.AssetUpdatedUtc)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This asset changed. Reload inventory before recording the relationship." }));
            return;
        }

        var head = await db.DrivePaths.Where(x => x.DiskId == id && x.AssetId == input.AssetId).MaxAsync(x => (long? )x.Id);
        if (head != input.PreviousReviewId)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This relationship changed. Reload the latest evidence and review it before saving." }));
            return;
        }

        var row = new DrivePathReview
        {
            DiskId = id,
            AssetId = asset.Id,
            RequestId = input.RequestId,
            AssetUpdatedUtc = asset.UpdatedUtc,
            AssetName = asset.Name,
            AssetType = asset.Type,
            ConfirmedIdentity = input.ConfirmedIdentity,
            DiskModel = disk.Model,
            Attached = input.Attached,
            Evidence = input.Evidence,
            ValidUntilUtc = input.ValidUntilUtc,
            RecordedUtc = now,
            Author = ctx.User.Identity!.Name!
        };
        db.DrivePaths.Add(row);
        db.Activity.Add(new() { Actor = row.Author, Action = "Drive hardware relationship recorded", Detail = JsonSerializer.Serialize(row) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(row));
        return;
    }
}
