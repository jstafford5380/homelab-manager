using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.GetCachedTemperatures;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/cached-temperatures");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Cached Temperatures");
        Description(b =>
        {
            b.WithName("StorageSmartGetCachedTemperatures");
            b.WithTags("Connections");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (connection is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync() ?? new OperatingSettings();
        var capability = await db.Capabilities.AsNoTracking().SingleOrDefaultAsync(x => x.ConnectionId == id && x.Name == "cached_temperatures");
        CachedTemperatureSnapshot? snapshot = null;
        try
        {
            if (connection.CachedTemperaturesJson is { } json)
                snapshot = JsonSerializer.Deserialize<CachedTemperatureSnapshot>(json);
        }
        catch (JsonException)
        {
        }

        await Send.ResultAsync(Results.Ok(new Response(settings.ReadCachedTemperatures, !settings.ReadCachedTemperatures ? "disabled" : !connection.Enabled ? "paused" : capability?.EffectiveStatus(DateTime.UtcNow) ?? "unavailable", capability?.LastAttemptUtc, capability?.LastSuccessUtc, snapshot)));
        return;
    }
}
