using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Health.Live;
public sealed class Endpoint : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/health/live");
        AllowAnonymous();
        Summary(s => s.Summary = "Live");
        Description(b =>
        {
            b.WithName("HealthLive");
            b.WithTags("Health");
            b.Produces<Response>(200);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(Results.Ok(new Response("alive")));
        return;
    }
}
