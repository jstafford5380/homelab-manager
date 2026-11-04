using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Findings.AcknowledgeFinding;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/findings/{id:guid}/acknowledge");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Acknowledge Finding");
        Description(b =>
        {
            b.WithName("FindingsAcknowledgeFinding");
            b.WithTags("Findings");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces(200);
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
        var ctx = HttpContext;
        var item = await db.Findings.FindAsync(id);
        if (item is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (item.State == "resolved")
        {
            await Send.ResultAsync(Results.Conflict(new { error = "Finding is already resolved." }));
            return;
        }

        item.AssignedOwner = ctx.User.Identity!.Name;
        item.State = "acknowledged";
        item.ReviewUtc = DateTime.UtcNow.AddDays(7);
        db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Finding acknowledged", Detail = item.Title });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
