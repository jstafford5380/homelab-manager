using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.PlanEditing;
using FastEndpoints;

namespace Homelab.Api.Features.Planning.UpdatePlan;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.PlanningItem>
{
    public override void Configure()
    {
        Put("/api/v1/plans/{id:guid}");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Update Plan");
        Description(b =>
        {
            b.WithName("PlanningUpdatePlan");
            b.WithTags("Planning");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.PlanningItem>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var input = request.Input;
        var context = HttpContext;
        var item = await db.Plans.FindAsync(id);
        if (item is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (item.Revision != input.Revision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This plan changed since you opened it. Your edits were not saved. Reopen the latest plan before trying again." }));
            return;
        }

        if (Homelab.Api.Planning.ValidatePlan(input)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        // A plan's physical-drive evidence cannot be silently reassigned during an edit.
        if (item.DiskId != input.DiskId)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "The linked physical drive cannot be changed. Create a separate plan for another drive." }));
            return;
        }

        var before = JsonSerializer.Serialize(item);
        item.Title = input.Title;
        item.Reason = input.Reason;
        item.Owner = input.Owner;
        item.CompletedUtc = input.Status == "complete" ? item.Status == "complete" ? item.CompletedUtc : DateTime.UtcNow : null;
        item.Status = input.Status;
        item.Budget = input.Budget;
        item.Currency = input.Currency;
        item.DueUtc = input.DueUtc;
        item.Revision = Guid.NewGuid();
        Record(db, item, before, context.User.Identity!.Name!, "Plan updated");
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(item));
        return;
    }
}
