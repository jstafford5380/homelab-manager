using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;
using static Homelab.Api.HardwareComponents;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.ReplaceComponent;
public sealed class Endpoint(HomelabDb db, IConfiguration config) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/api/v1/hardware/servers/{id:guid}/components/{componentId:guid}/replace");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Replace Component");
        Description(b =>
        {
            b.WithName("HardwareComponentsReplaceComponent");
            b.WithTags("Hardware");
            b.Accepts<Request>("application/json");
            b.Produces<Response>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var componentId = request.ComponentId;
        var input = request.Input;
        if (await Server(id, db, config)is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var c = await db.HardwareComponents.SingleOrDefaultAsync(c => c.Id == componentId && c.ServerId == id && c.RetiredUtc == null);
        if (c is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (c.Revision != input.Revision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This component changed. Reload before replacing." }));
            return;
        }

        if (Validate(input.Successor, DateTime.UtcNow)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        if (input.Successor.UpsDeviceKey is not null)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Link the replacement UPS after saving it." }));
            return;
        }

        if (c.Kind != input.Successor.Kind)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "The successor must be the same kind of component." }));
            return;
        }

        var next = new HardwareComponent
        {
            ServerId = id,
            Kind = c.Kind,
            PredecessorId = c.Id
        };
        Apply(next, input.Successor);
        next.InstalledUtc ??= DateTime.UtcNow;
        // Transfer the detected identity to the successor; the retired record keeps its final detected values and history.
        next.DetectedKey = c.DetectedKey; // Reserved detection slot; the next successful inventory must establish successor values.
        c.DetectedKey = null;
        c.RetiredUtc = DateTime.UtcNow;
        c.Revision = Guid.NewGuid();
        db.HardwareEvents.Add(new() { ComponentId = c.Id, Kind = "replaced", Text = "Replaced by " + next.Name + " (" + next.Id + ")." });
        db.HardwareEvents.Add(new() { ComponentId = next.Id, Kind = "installed", Text = "Replaced " + (c.Name.Length > 0 ? c.Name : c.DetectedName) + " (" + c.Id + ")." });
        db.HardwareComponents.Add(next);
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(new Response(next.Id)));
        return;
    }
}
