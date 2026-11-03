using Microsoft.EntityFrameworkCore;
using static Homelab.Api.RecoveryRules;
using FastEndpoints;

namespace Homelab.Api.Features.Recovery.CreatePolicy;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.RecoveryPolicy>
{
    public override void Configure()
    {
        Post("/api/v1/recovery");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Create Policy");
        Description(b =>
        {
            b.WithName("RecoveryCreatePolicy");
            b.WithTags("Recovery");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.RecoveryPolicy>(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        var ctx = HttpContext;
        if (ValidatePolicy(input)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        input.Id = Guid.NewGuid();
        input.Revision = Guid.NewGuid();
        input.Owner = ctx.User.Identity!.Name!;
        input.RestoreResult = "unverified";
        input.RestoreTestUtc = null;
        input.RecoveryPointUtc = null;
        db.Recovery.Add(input);
        db.Activity.Add(new() { Actor = input.Owner, Action = "Protection policy created", Detail = input.Asset });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(input));
        return;
    }
}
