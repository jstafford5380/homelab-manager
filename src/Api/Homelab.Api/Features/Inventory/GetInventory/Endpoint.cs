using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Inventory.GetInventory;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/inventory");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Inventory");
        Description(b =>
        {
            b.WithName("GetInventory");
            b.WithTags("Inventory");
            b.Produces<Response>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(Results.Ok(new Response(await db.Assets.OrderBy(x => x.Name).ToListAsync(), await db.Dependencies.ToListAsync())));
        return;
    }
}
