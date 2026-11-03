using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.GetHistory;
public sealed class Endpoint(HomelabDb db, IConfiguration config) : Endpoint<Request, Homelab.Api.HardwareEvent[]>
{
    public override void Configure()
    {
        Get("/api/v1/hardware/servers/{id:guid}/components/{componentId:guid}/history");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("HardwareComponentsGetHistory");
            b.WithTags("Hardware");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Homelab.Api.HardwareEvent[]>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var componentId = request.ComponentId;
        await Send.ResultAsync(await Server(id, db, config)is null || !await db.HardwareComponents.AnyAsync(c => c.ServerId == id && c.Id == componentId) ? Results.NotFound() : Results.Ok(await db.HardwareEvents.AsNoTracking().Where(e => e.ComponentId == componentId).OrderByDescending(e => e.AtUtc).Take(100).ToArrayAsync()));
        return;
    }
}
