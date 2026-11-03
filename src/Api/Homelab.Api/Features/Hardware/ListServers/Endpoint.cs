using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.ListServers;
public sealed class Endpoint(HomelabDb db, IConfiguration config, WriteCoordinator writes) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/hardware/servers");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "List Servers");
        Description(b =>
        {
            b.WithName("HardwareListServers");
            b.WithTags("Hardware");
            b.Produces<Response>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        using (var lease = await writes.EnterAsync(ct))
            await EnsureServers(db, ct);
        var servers = await db.HardwareServers.AsNoTracking().Where(s => s.Platform != "synology" || SynologyEnabled(config)).OrderBy(s => s.Name).ToArrayAsync(ct);
        var list = new List<HardwareServerView>();
        foreach (var s in servers)
            list.Add((await HardwareMetrics.Detail(db, s, DateTime.UtcNow, 30, MetricsEnabled(config) || s.Platform == "synology", ct)).Server);
        await Send.ResultAsync(Results.Ok(new Response(MetricsEnabled(config), SynologyEnabled(config), list)));
        return;
    }
}
