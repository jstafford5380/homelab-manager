using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Findings.UnsnoozeFinding;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/findings/{id:guid}/unsnooze");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Unsnooze Finding");
        Description(b =>
        {
            b.WithName("FindingsUnsnoozeFinding");
            b.WithTags("Findings");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var ctx = HttpContext;
        var finding = await db.Findings.FindAsync(id);
        if (finding is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        finding.SnoozedUntilUtc = null;
        finding.ReminderSequence++;
        db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Finding snooze cleared", Detail = id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
