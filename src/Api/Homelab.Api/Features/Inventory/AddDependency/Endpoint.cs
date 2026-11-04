using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Inventory.AddDependency;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.AssetDependency>
{
    public override void Configure()
    {
        Post("/api/v1/inventory/dependencies");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Add Dependency");
        Description(b =>
        {
            b.WithName("InventoryAddDependency");
            b.WithTags("Inventory");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.AssetDependency>(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        var ctx = HttpContext;
        if (input.Evidence is null || input.Evidence.Length > 10000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Relationship evidence must be text with at most 10000 characters; leave it empty if unknown." }));
            return;
        }

        if (input.SourceId == input.TargetId || input.Relationship is not ("runs_on" or "stores_on" or "backed_up_to" or "powered_by" or "connected_to") || input.Verification is not ("unverified" or "verified") || input.Verification == "verified" && string.IsNullOrWhiteSpace(input.Evidence))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose distinct assets, a supported relationship, and evidence for verified links." }));
            return;
        }

        if (await db.Assets.CountAsync(a => (a.Id == input.SourceId || a.Id == input.TargetId) && !a.Archived) != 2)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Both assets must exist and be active." }));
            return;
        }

        var existing = await db.Dependencies.SingleOrDefaultAsync(d => d.SourceId == input.SourceId && d.TargetId == input.TargetId && d.Relationship == input.Relationship);
        input.Id = existing?.Id ?? Guid.NewGuid();
        input.Author = ctx.User.Identity!.Name!;
        input.UpdatedUtc = DateTime.UtcNow;
        if (existing is null)
            db.Dependencies.Add(input);
        else
            db.Entry(existing).CurrentValues.SetValues(input);
        db.Activity.Add(new() { Actor = input.Author, Action = "Dependency recorded", Detail = input.Relationship });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(input));
        return;
    }
}
