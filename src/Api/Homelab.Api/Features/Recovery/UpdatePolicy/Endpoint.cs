using Microsoft.EntityFrameworkCore;
using static Homelab.Api.RecoveryRules;
using FastEndpoints;

namespace Homelab.Api.Features.Recovery.UpdatePolicy;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.RecoveryPolicy>
{
    public override void Configure()
    {
        Put("/api/v1/recovery/{id:guid}");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Policy");
        Description(b =>
        {
            b.WithName("RecoveryUpdatePolicy");
            b.WithTags("Recovery");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.RecoveryPolicy>(200);
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
        var policy = await db.Recovery.FindAsync(id);
        if (policy is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (input.Revision != policy.Revision)
        {
            await Send.ResultAsync(StalePolicy());
            return;
        }

        if (ValidatePolicy(input)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        input.Id = id;
        input.Revision = Guid.NewGuid();
        input.Owner = ctx.User.Identity!.Name!;
        input.RestoreResult = "unverified";
        input.RestoreTestUtc = null;
        input.RecoveryPointUtc = null;
        db.Entry(policy).CurrentValues.SetValues(input);
        db.Activity.Add(new() { Actor = input.Owner, Action = "Protection policy revised; evidence requires revalidation", Detail = input.Asset });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(policy));
        return;
    }
}
