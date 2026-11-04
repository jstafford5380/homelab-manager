using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DiskMetadataRules;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateMetadata;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.Disk>
{
    public override void Configure()
    {
        Put("/api/v1/disks/{id:guid}/metadata");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Update Metadata");
        Description(b =>
        {
            b.WithName("StorageDrivesUpdateMetadata");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.Disk>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var input = request.Input;
        var context = HttpContext;
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var error = ValidateMetadata(disk, input);
        if (error is not null)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        var before = JsonSerializer.Serialize(disk);
        disk.Bay = input.Bay;
        disk.BayVerified = input.BayVerified;
        disk.BayEvidence = input.BayVerified ? input.BayEvidence : null;
        disk.BayVerifiedUtc = input.BayVerified ? DateTime.UtcNow : null;
        disk.BayConfirmedIdentity = input.BayVerified ? StorageInventory.Identity(disk) : null;
        disk.PurchasedUtc = input.PurchasedUtc;
        disk.PurchaseDateConfidence = input.PurchaseDateConfidence;
        disk.WarrantyEndUtc = input.WarrantyEndUtc;
        disk.Notes = input.Notes;
        disk.Lifecycle = input.Lifecycle;
        db.DiskAnnotations.Add(new() { DiskId = id, Actor = context.User.Identity!.Name!, BeforeJson = before, AfterJson = JsonSerializer.Serialize(disk) });
        db.Activity.Add(new() { Actor = context.User.Identity.Name!, Action = "Disk metadata updated", Detail = id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(disk));
        return;
    }
}
