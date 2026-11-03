using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.AddNote;
public sealed class Endpoint(HomelabDb db, IConfiguration config) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/hardware/servers/{id:guid}/components/{componentId:guid}/notes");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Add Note");
        Description(b =>
        {
            b.WithName("HardwareComponentsAddNote");
            b.WithTags("Hardware");
            b.Accepts<Request>("application/json");
            b.Produces(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var componentId = request.ComponentId;
        var input = request.Input;
        if (await Server(id, db, config)is null || !await db.HardwareComponents.AnyAsync(c => c.ServerId == id && c.Id == componentId))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 2000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Enter a note of up to 2,000 characters." }));
            return;
        }

        db.HardwareEvents.Add(new() { ComponentId = componentId, Text = input.Text });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
