using Microsoft.EntityFrameworkCore;
using static Homelab.Api.Replacements;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.RecordReplacement;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DiskReplacement>
{
    public override void Configure()
    {
        Post("/api/v1/replacements");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Record Replacement");
        Description(b =>
        {
            b.WithName("StorageDrivesRecordReplacement");
            b.WithTags("Replacements");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.DiskReplacement>(200);
            b.Produces<ApiError>(400);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        var context = HttpContext;
        var oldDisk = await db.Disks.FindAsync(input.OldDiskId);
        var newDisk = await db.Disks.FindAsync(input.NewDiskId);
        if (oldDisk is null || newDisk is null)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Both physical drives must already exist in inventory." }));
            return;
        }

        var now = DateTime.UtcNow;
        if (Validate(oldDisk, newDisk, input, now)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        if (await db.Replacements.AnyAsync(r => r.OldDiskId == input.OldDiskId && r.NewDiskId == input.NewDiskId && r.OccurredUtc == input.OccurredUtc && r.VoidedUtc == null))
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This replacement has already been recorded." }));
            return;
        }

        var record = Create(oldDisk, newDisk, input, context.User.Identity!.Name!, now);
        db.Replacements.Add(record);
        // Recording a completed replacement also starts the new drive's service plan.
        // Acquisition and replacement are committed together; no NAS mutation occurs.
        var acquisition = await db.DriveAcquisitions.SingleOrDefaultAsync(x => x.DiskId == newDisk.Id);
        if (acquisition is null)
        {
            acquisition = new()
            {
                DiskId = newDisk.Id
            };
            db.DriveAcquisitions.Add(acquisition);
        }

        acquisition.Revision = Guid.NewGuid();
        acquisition.InstalledUtc = input.OccurredUtc;
        acquisition.InstallationConfidence = input.DateConfidence;
        acquisition.InstallationEvidence = input.Evidence;
        acquisition.Author = record.Author;
        acquisition.RecordedUtc = now;
        db.Activity.Add(new() { Actor = record.Author, Action = "Replacement history recorded locally", Detail = record.Id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(record));
        return;
    }
}
