using Microsoft.EntityFrameworkCore;
using static Homelab.Api.PoolCapacity;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Pools.GetCapacity;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.CapacityEstimate>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/pools/{guid}/capacity");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Capacity");
        Description(b =>
        {
            b.WithName("StoragePoolsGetCapacity");
            b.WithTags("Connections");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Homelab.Api.CapacityEstimate>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var guid = request.Guid;
        var days = request.Days;
        var window = days ?? 30;
        if (window is not (7 or 30 or 90))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose 7, 30 or 90 days." }));
            return;
        }

        var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (connection is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var now = DateTime.UtcNow;
        var assessment = new Window(window, now, TimeSpan.FromSeconds(Math.Max(300, connection.PollSeconds * 3)));
        await foreach (var row in db.PoolSnapshots.AsNoTracking().Where(x => x.ConnectionId == id && x.PoolGuid == guid && x.FetchedUtc >= now.AddDays(-window)).OrderBy(x => x.FetchedUtc).Take(300001).AsAsyncEnumerable().WithCancellation(ct))
            assessment.Add(row);
        var result = assessment.Finish();
        var capability = await db.Capabilities.AsNoTracking().SingleOrDefaultAsync(x => x.ConnectionId == id && x.Name == "pools", ct);
        await Send.ResultAsync(Results.Ok(RequireCurrentContext(result, connection, capability, guid, now)));
        return;
    }
}
