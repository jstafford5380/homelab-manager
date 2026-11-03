using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateProfile;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DriveAcquisition>
{
    public override void Configure()
    {
        Put("/api/v1/storage/drives/{id:guid}/profile");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Profile");
        Description(b =>
        {
            b.WithName("StorageDrivesUpdateProfile");
            b.WithTags("Storage");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.DriveAcquisition>(200);
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
        var context = HttpContext;
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var record = await db.DriveAcquisitions.SingleOrDefaultAsync(x => x.DiskId == id);
        if (record?.Revision != input.Revision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This drive changed. Reload its details before saving." }));
            return;
        }

        if (input.Nickname?.Length > 200 || input.Notes is null || input.Notes.Length > 10000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Keep names under 200 characters and notes under 10,000 characters." }));
            return;
        }

        var now = DateTime.UtcNow;
        var confidence = input.DatesEstimated ? "estimated" : "documented";
        var acquisitionInput = new DriveAcquisitionInput(input.Revision, input.InstalledUtc, input.InstalledUtc is null ? "unknown" : confidence, input.InstalledUtc is null ? "" : "Owner recorded in Storage.", input.PurchaseCost, input.PurchaseCost is null ? "" : input.Currency, input.PurchaseCost is null ? "unknown" : "documented", input.PurchaseCost is null ? "" : "Owner recorded in Storage.");
        if (DriveAcquisitions.Validate(acquisitionInput, now)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        if (input.PurchasedUtc is { } purchase && (purchase.Kind != DateTimeKind.Utc || purchase < DateTime.UnixEpoch || purchase > now) || input.WarrantyEndUtc is { } warranty && (warranty.Kind != DateTimeKind.Utc || warranty < DateTime.UnixEpoch))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Enter valid dates; purchase and installation cannot be in the future." }));
            return;
        }

        var before = JsonSerializer.Serialize(new { disk, acquisition = record });
        disk.Nickname = string.IsNullOrWhiteSpace(input.Nickname) ? null : input.Nickname.Trim();
        disk.PurchasedUtc = input.PurchasedUtc;
        disk.PurchaseDateConfidence = input.PurchasedUtc is null ? "unknown" : confidence;
        disk.WarrantyEndUtc = input.WarrantyEndUtc;
        disk.Notes = input.Notes;
        if (record is null)
        {
            record = new()
            {
                DiskId = id
            };
            db.DriveAcquisitions.Add(record);
        }

        record.Revision = Guid.NewGuid();
        record.InstalledUtc = input.InstalledUtc;
        record.InstallationConfidence = acquisitionInput.InstallationConfidence;
        record.InstallationEvidence = acquisitionInput.InstallationEvidence;
        record.PurchaseCost = input.PurchaseCost;
        record.Currency = acquisitionInput.Currency;
        record.CostConfidence = acquisitionInput.CostConfidence;
        record.PurchaseEvidence = acquisitionInput.PurchaseEvidence;
        record.Author = context.User.Identity!.Name!;
        record.RecordedUtc = now;
        db.DiskAnnotations.Add(new() { DiskId = id, Actor = record.Author, BeforeJson = before, AfterJson = JsonSerializer.Serialize(new { disk, acquisition = record }) });
        db.Activity.Add(new() { Actor = record.Author, Action = "Storage drive profile updated", Detail = id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(record));
        return;
    }
}
