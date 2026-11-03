using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Authentication.GetSession;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/session");
        AllowAnonymous();
        Summary(s => s.Summary = "Get Session");
        Description(b =>
        {
            b.WithName("GetSession");
            b.WithTags("Authentication");
            b.Produces<Response>(200);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var ctx = HttpContext;
        await Send.ResultAsync(Results.Ok(new Response(ctx.User.Identity?.IsAuthenticated ?? false, !await db.Owners.AnyAsync())));
        return;
    }
}
