using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Synology.GetSettings;
public sealed class Endpoint(HomelabDb db, IConfiguration config) : Endpoint<EmptyRequest, ResponseItem[]>
{
    public override void Configure()
    {
        Get("/api/v1/hardware/synology/settings");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Get Settings");
        Description(b =>
        {
            b.WithName("HardwareSynologyGetSettings");
            b.WithTags("Hardware");
            b.Produces<ResponseItem[]>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(!HardwareInventory.SynologyEnabled(config) ? Results.NotFound() : Results.Ok(await db.HardwareServers.AsNoTracking().Where(s => s.Platform == "synology").Select(s => new ResponseItem(s.Id, s.Name, s.Host, s.Connected, s.ProtectedCommunity != null)).ToArrayAsync()));
        return;
    }
}
