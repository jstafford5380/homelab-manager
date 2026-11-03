using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.VoidMaintenance;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DriveMaintenance>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/maintenance/{recordId:long}/void");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Void Maintenance");
        Description(b =>
        {
            b.WithName("StorageDrivesVoidMaintenance");
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
        var recordId = request.RecordId;
        var input = request.Input;
        var context = HttpContext;
        var record = await db.Maintenance.SingleOrDefaultAsync(x => x.Id == recordId && x.DiskId == id);
        if (record is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Record why this entry is incorrect, in up to 2,000 characters." }));
            return;
        }

        if (record.VoidedUtc is not null)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This entry has already been marked incorrect. Its original evidence is retained." }));
            return;
        }

        record.VoidedUtc = DateTime.UtcNow;
        record.VoidReason = input.Reason;
        record.VoidedBy = context.User.Identity!.Name!;
        db.Activity.Add(new() { Actor = record.VoidedBy, Action = "Drive maintenance marked incorrect", Detail = record.Id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(record));
        return;
    }
}
