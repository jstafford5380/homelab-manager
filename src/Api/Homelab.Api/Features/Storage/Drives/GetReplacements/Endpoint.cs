using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetReplacements;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, System.Collections.Generic.List<Homelab.Api.DiskReplacement>>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/replacements");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Replacements");
        Description(b =>
        {
            b.WithName("StorageDrivesGetReplacements");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<System.Collections.Generic.List<Homelab.Api.DiskReplacement>>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        if (!await db.Disks.AnyAsync(d => d.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        await Send.ResultAsync(Results.Ok(await db.Replacements.AsNoTracking().Where(r => r.OldDiskId == id || r.NewDiskId == id).OrderByDescending(r => r.OccurredUtc).ToListAsync()));
        return;
    }
}
