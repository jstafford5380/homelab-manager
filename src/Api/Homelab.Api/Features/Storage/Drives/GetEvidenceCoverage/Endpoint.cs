using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DriveEvidence;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetEvidenceCoverage;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/evidence-coverage");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Evidence Coverage");
        Description(b =>
        {
            b.WithName("StorageDrivesGetEvidenceCoverage");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        if (!await db.Disks.AnyAsync(x => x.Id == id, ct))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new OperatingSettings();
        var now = DateTime.UtcNow;
        await Send.ResultAsync(Results.Ok(new Response(now, settings.SmartMinutes * 3, await ReadAsync(db, id, now, settings.SmartMinutes, ct))));
        return;
    }
}
