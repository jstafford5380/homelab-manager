using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Authentication.SignOut;
public sealed class Endpoint : Endpoint<EmptyRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/logout");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Sign Out");
        Description(b =>
        {
            b.WithName("AuthenticationSignOut");
            b.WithTags("Authentication");
            b.Produces(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var ctx = HttpContext;
        await ctx.SignOutAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
