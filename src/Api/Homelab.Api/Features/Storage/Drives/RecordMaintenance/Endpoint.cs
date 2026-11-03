using Microsoft.EntityFrameworkCore;
using static Homelab.Api.MaintenanceHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.RecordMaintenance;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DriveMaintenance>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/maintenance");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Record Maintenance");
        Description(b =>
        {
            b.WithName("StorageDrivesRecordMaintenance");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.DriveMaintenance>(200);
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

        var existing = await db.Maintenance.SingleOrDefaultAsync(x => x.DiskId == id && x.RequestId == input.RequestId);
        if (existing is not null)
        {
            await Send.ResultAsync(SameSubmission(existing, input) ? Results.Ok(existing) : Results.Conflict(new { error = "This request was already recorded with different details. Reload the history before recording another event." }));
            return;
        }

        if (Validate(disk, input, DateTime.UtcNow)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        var record = new DriveMaintenance
        {
            DiskId = id,
            RequestId = input.RequestId,
            OccurredUtc = input.OccurredUtc,
            DateConfidence = input.DateConfidence,
            Kind = input.Kind,
            Outcome = input.Outcome,
            Summary = input.Summary,
            Evidence = input.Evidence,
            ConfirmedIdentity = input.ConfirmedIdentity,
            Author = context.User.Identity!.Name!,
            RecordedUtc = DateTime.UtcNow
        };
        db.Maintenance.Add(record);
        db.Activity.Add(new() { Actor = record.Author, Action = "Drive maintenance recorded", Detail = $"{id}: {input.Kind}; manual evidence, no NAS action" });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(record));
        return;
    }
}
