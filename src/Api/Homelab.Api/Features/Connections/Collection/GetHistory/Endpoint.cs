using Microsoft.EntityFrameworkCore;
using static Homelab.Api.CollectionHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.Collection.GetHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/collection-history");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("ConnectionsCollectionGetHistory");
            b.WithTags("Connections");
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
        var before = request.Before;
        if (!await db.Connections.AnyAsync(x => x.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var rows = await db.CollectionRuns.AsNoTracking().Where(x => x.ConnectionId == id && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(51).ToListAsync();
        await Send.ResultAsync(Results.Ok(new Response(rows.Take(50).Select(x => new ResponseItemsItem(x.Id, x.StartedUtc, x.EndedUtc, x.Result, Duration(x), Coverage(x.CapabilitySummaryJson))), rows.Count > 50 ? (long? )rows[49].Id : null)));
        return;
    }
}
