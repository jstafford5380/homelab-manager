using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.GetInventory;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/storage");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Inventory");
        Description(b =>
        {
            b.WithName("StorageGetInventory");
            b.WithTags("Storage");
            b.Produces<Response>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var capabilities = await db.Capabilities.AsNoTracking().ToListAsync();
        var now = DateTime.UtcNow;
        await Send.ResultAsync(Results.Ok(new Response((await db.Connections.AsNoTracking().ToListAsync()).Select(x => new ResponseSystemsItem(x.Id, x.Name, x.Status, x.LastSuccessUtc, x.TopologyJson, PoolRedundancy.FromStored(x.TopologyJson, x.Enabled && capabilities.Any(c => c.ConnectionId == x.Id && c.Name == "pools" && c.EffectiveStatus(now) == "supported")))), capabilities.Select(x => new ResponseCapabilitiesItem(x.ConnectionId, x.Name, x.EffectiveStatus(now), x.Detail, x.LastSuccessUtc)), await db.NativeAlerts.OrderByDescending(x => x.LastSeenUtc).Take(200).ToListAsync())));
        return;
    }
}
