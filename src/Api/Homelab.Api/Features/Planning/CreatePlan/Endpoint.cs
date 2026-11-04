using Microsoft.EntityFrameworkCore;
using static Homelab.Api.PlanEditing;
using FastEndpoints;

namespace Homelab.Api.Features.Planning.CreatePlan;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.PlanningItem>
{
    public override void Configure()
    {
        Post("/api/v1/plans");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Create Plan");
        Description(b =>
        {
            b.WithName("PlanningCreatePlan");
            b.WithTags("Planning");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.PlanningItem>(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var item = request.Input;
        var context = HttpContext;
        if (Homelab.Api.Planning.ValidatePlan(item)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        if (item.DiskId is { } disk && !await db.Disks.AnyAsync(x => x.Id == disk))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Disk does not exist." }));
            return;
        }

        item.CompletedUtc = item.Status == "complete" ? DateTime.UtcNow : null;
        item.Id = Guid.NewGuid();
        item.Revision = Guid.NewGuid();
        db.Plans.Add(item);
        Record(db, item, null, context.User.Identity!.Name!, "Plan created");
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(item));
        return;
    }
}
