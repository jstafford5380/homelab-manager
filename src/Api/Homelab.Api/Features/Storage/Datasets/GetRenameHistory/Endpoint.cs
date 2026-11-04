using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.GetRenameHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/dataset-renames");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Rename History");
        Description(b =>
        {
            b.WithName("StorageDatasetsGetRenameHistory");
            b.WithTags("Connections");
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
        if (before <= 0)
        {
            await Send.ResultAsync(Results.BadRequest());
            return;
        }

        if (!await db.Connections.AnyAsync(x => x.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var rows = await db.DatasetRenames.AsNoTracking().Where(x => x.ConnectionId == id && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(26).ToArrayAsync();
        await Send.ResultAsync(Results.Ok(new Response(rows.Take(25), rows.Length > 25 ? (long? )rows[24].Id : null)));
        return;
    }
}
