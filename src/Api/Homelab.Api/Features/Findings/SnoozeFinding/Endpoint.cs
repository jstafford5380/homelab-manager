using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Findings.SnoozeFinding;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/findings/{id:guid}/snooze");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Snooze Finding");
        Description(b =>
        {
            b.WithName("FindingsSnoozeFinding");
            b.WithTags("Findings");
            b.Accepts<Request>("application/json");
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
        var input = request.Input;
        var ctx = HttpContext;
        if (input.UntilUtc <= DateTime.UtcNow || input.UntilUtc > DateTime.UtcNow.AddDays(7))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Snooze must end within seven days." }));
            return;
        }

        var finding = await db.Findings.FindAsync(id);
        if (finding is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (finding.State == "resolved")
        {
            await Send.ResultAsync(Results.Conflict(new { error = "Resolved findings cannot be snoozed." }));
            return;
        }

        finding.SnoozedUntilUtc = input.UntilUtc;
        finding.AssignedOwner = ctx.User.Identity!.Name;
        db.Activity.Add(new() { Actor = ctx.User.Identity.Name!, Action = "Finding snoozed", Detail = id + " until " + input.UntilUtc.ToString("O") });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
