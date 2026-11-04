using Microsoft.EntityFrameworkCore;
using static Homelab.Api.InventoryRules;
using FastEndpoints;

namespace Homelab.Api.Features.Inventory.AddAsset;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.InventoryAsset>
{
    public override void Configure()
    {
        Post("/api/v1/inventory/assets");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Add Asset");
        Description(b =>
        {
            b.WithName("InventoryAddAsset");
            b.WithTags("Inventory");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.InventoryAsset>(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        var ctx = HttpContext;
        if (ValidateAsset(input)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        input.Id = Guid.NewGuid();
        input.UpdatedUtc = DateTime.UtcNow;
        db.Assets.Add(input);
        db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Inventory asset created", Detail = input.Name });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(input));
        return;
    }
}
