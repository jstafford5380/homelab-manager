using FastEndpoints;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api.Features.Connections.RefreshNow;

public sealed class Endpoint(Collector collector, SynologyHardwareCollector synology, HomelabDb db)
    : Endpoint<Request>
{
    public override void Configure()
    {
        Post("/api/v1/connections/{id:guid}/refresh");
        AuthSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
        Options(builder => builder.WithMetadata(new OwnsWriteLease()));
        Summary(summary => summary.Summary = "Refresh a server now");
        Description(builder => builder
            .WithName("RefreshServerNow")
            .WithTags("Connections")
            .Produces(204)
            .Produces<ApiError>(400)
            .Produces(401)
            .Produces(403)
            .Produces(404)
            .Produces<ApiError>(409)
            .Produces<ApiError>(502), clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        if (request.Target == "hardware" &&
            await db.HardwareServers.AnyAsync(server => server.Id == request.Id && server.Platform == "synology", ct))
        {
            await Send.ResultAsync(await synology.RefreshNowAsync(request.Id, ct));
            return;
        }

        if (request.Target is not ("storage" or "hardware"))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose Storage or Hardware to refresh." }));
            return;
        }

        var target = request.Target == "storage" ? Collector.RefreshTarget.Storage : Collector.RefreshTarget.Hardware;
        await Send.ResultAsync(await collector.RefreshNowAsync(request.Id, target, ct));
    }
}
