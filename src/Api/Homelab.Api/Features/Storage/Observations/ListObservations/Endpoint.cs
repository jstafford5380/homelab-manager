using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Observations.ListObservations;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, System.Collections.Generic.List<Homelab.Api.Observation>>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/observations");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "List Observations");
        Description(b =>
        {
            b.WithName("StorageObservationsListObservations");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<System.Collections.Generic.List<Homelab.Api.Observation>>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        await Send.ResultAsync(Results.Ok(await db.Observations.Where(x => x.DiskId == id).OrderByDescending(x => x.Id).Take(1000).ToListAsync()));
        return;
    }
}
