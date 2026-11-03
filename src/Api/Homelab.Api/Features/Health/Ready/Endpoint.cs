using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Health.Ready;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/health/ready");
        AllowAnonymous();
        Summary(s => s.Summary = "Ready");
        Description(b =>
        {
            b.WithName("HealthReady");
            b.WithTags("Health");
            b.Produces<Response>(200);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(await db.Database.CanConnectAsync() ? Results.Ok(new Response("ready")) : Results.StatusCode(503));
        return;
    }
}
