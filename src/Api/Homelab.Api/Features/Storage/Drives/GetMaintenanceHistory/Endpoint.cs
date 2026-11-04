using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetMaintenanceHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/maintenance");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Maintenance History");
        Description(b =>
        {
            b.WithName("StorageDrivesGetMaintenanceHistory");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var before = request.Before;
        var days = request.Days;
        if (!await db.Disks.AnyAsync(x => x.Id == id, ct))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (before is <= 0 || days is not (null or 0 or 1 or 7 or 30))
        {
            await Send.ResultAsync(Results.BadRequest());
            return;
        }

        var cutoff = days is null or 0 ? DateTime.UnixEpoch : DateTime.UtcNow.AddDays(-days.Value);
        var rows = await db.Maintenance.AsNoTracking().Where(x => x.DiskId == id && x.OccurredUtc >= cutoff && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(26).ToListAsync(ct);
        await Send.ResultAsync(Results.Ok(new Response(rows.Take(25), rows.Count > 25 ? (long? )rows[24].Id : null)));
        return;
    }
}
