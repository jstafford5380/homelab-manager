using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.GetServer;
public sealed class Endpoint(HomelabDb db, IConfiguration config, WriteCoordinator writes) : Endpoint<Request, Homelab.Api.HardwareDetail>
{
    public override void Configure()
    {
        Get("/api/v1/hardware/servers/{id:guid}");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Server");
        Description(b =>
        {
            b.WithName("HardwareGetServer");
            b.WithTags("Hardware");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Homelab.Api.HardwareDetail>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var range = request.Range;
        if (range is not (null or "24h" or "7d" or "30d" or "90d"))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose 24h, 7d, 30d or 90d." }));
            return;
        }

        if (!await db.HardwareServers.AnyAsync(s => s.Id == id, ct))
        {
            using var lease = await writes.EnterAsync(ct);
            await EnsureServers(db, ct);
        }

        var s = await Server(id, db, config);
        if (s is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var detail = await HardwareMetrics.Detail(db, s, DateTime.UtcNow,
            range == "24h" ? 1 : range == "7d" ? 7 : range == "90d" ? 90 : 30,
            MetricsEnabled(config) || s.Platform == "synology", ct);
        await Send.ResultAsync(Results.Ok(detail with { Investment = await InvestmentTotal.Hardware(db, ct) }));
    }
}
