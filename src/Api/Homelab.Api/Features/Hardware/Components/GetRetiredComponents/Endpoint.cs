using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.GetRetiredComponents;
public sealed class Endpoint(HomelabDb db, IConfiguration config) : Endpoint<Request, Homelab.Api.HardwareComponent[]>
{
    public override void Configure()
    {
        Get("/api/v1/hardware/servers/{id:guid}/retired");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Retired Components");
        Description(b =>
        {
            b.WithName("HardwareComponentsGetRetiredComponents");
            b.WithTags("Hardware");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Homelab.Api.HardwareComponent[]>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        await Send.ResultAsync(await Server(id, db, config)is null ? Results.NotFound() : Results.Ok(await db.HardwareComponents.AsNoTracking().Where(c => c.ServerId == id && c.RetiredUtc != null).ToArrayAsync()));
        return;
    }
}
