using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.InventoryRules;
using FastEndpoints;

namespace Homelab.Api.Features.Inventory.UpdateAsset;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.InventoryAsset>
{
    public override void Configure()
    {
        Put("/api/v1/inventory/assets/{id:guid}");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Update Asset");
        Description(b =>
        {
            b.WithName("InventoryUpdateAsset");
            b.WithTags("Inventory");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.InventoryAsset>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var input = request.Input;
        var ctx = HttpContext;
        var asset = await db.Assets.FindAsync(id);
        if (asset is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (ValidateAsset(input)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        input.Id = id;
        input.UpdatedUtc = DateTime.UtcNow;
        db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Inventory asset updated", Detail = JsonSerializer.Serialize(new { id, before = asset, after = input }) });
        db.Entry(asset).CurrentValues.SetValues(input);
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(asset));
        return;
    }
}
