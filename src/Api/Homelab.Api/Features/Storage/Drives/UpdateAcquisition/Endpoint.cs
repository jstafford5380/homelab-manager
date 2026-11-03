using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DriveAcquisitions;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateAcquisition;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DriveAcquisition>
{
    public override void Configure()
    {
        Put("/api/v1/disks/{id:guid}/acquisition");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Acquisition");
        Description(b =>
        {
            b.WithName("StorageDrivesUpdateAcquisition");
            b.WithTags("Storage.Drives");
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
        if (!await db.Disks.AnyAsync(x => x.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var record = await db.DriveAcquisitions.SingleOrDefaultAsync(x => x.DiskId == id);
        if (input.Revision != record?.Revision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "These acquisition notes changed. Your draft was not saved. Reload the saved notes and review them before trying again." }));
            return;
        }

        if (Validate(input, DateTime.UtcNow)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        var before = record is null ? null : JsonSerializer.Serialize(record);
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
        record.InstallationConfidence = input.InstallationConfidence;
        record.InstallationEvidence = input.InstallationEvidence;
        record.PurchaseCost = input.PurchaseCost;
        record.Currency = input.Currency.ToUpperInvariant();
        record.CostConfidence = input.CostConfidence;
        record.PurchaseEvidence = input.PurchaseEvidence;
        record.Author = context.User.Identity!.Name!;
        record.RecordedUtc = DateTime.UtcNow;
        db.DiskAnnotations.Add(new() { DiskId = id, Actor = record.Author, BeforeJson = before ?? "null", AfterJson = JsonSerializer.Serialize(record) });
        db.Activity.Add(new() { Actor = record.Author, Action = "Drive acquisition notes updated", Detail = id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(record));
        return;
    }
}
