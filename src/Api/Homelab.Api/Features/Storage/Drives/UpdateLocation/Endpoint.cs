using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateLocation;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Put("/api/v1/storage/drives/{id:guid}/location");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Location");
        Description(b =>
        {
            b.WithName("StorageDrivesUpdateLocation");
            b.WithTags("Storage");
            b.Accepts<Request>("application/json");
            b.Produces(200);
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

        var metadata = new DiskMetadata(input.Bay, true, input.ConfirmedSerial, input.Evidence, disk.PurchasedUtc, disk.PurchaseDateConfidence, disk.WarrantyEndUtc, disk.Notes, disk.Lifecycle);
        if (DiskMetadataRules.ValidateMetadata(disk, metadata)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        var before = JsonSerializer.Serialize(disk);
        disk.Bay = input.Bay;
        disk.BayVerified = true;
        disk.BayVerifiedUtc = DateTime.UtcNow;
        disk.BayEvidence = input.Evidence;
        disk.BayConfirmedIdentity = StorageInventory.Identity(disk);
        db.DiskAnnotations.Add(new() { DiskId = id, Actor = context.User.Identity!.Name!, BeforeJson = before, AfterJson = JsonSerializer.Serialize(disk) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
