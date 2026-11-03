using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.GetInventory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/datasets");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Inventory");
        Description(b =>
        {
            b.WithName("StorageDatasetsGetInventory");
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
        var nas = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (nas is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new OperatingSettings();
        var capability = await db.Capabilities.AsNoTracking().SingleOrDefaultAsync(x => x.ConnectionId == id && x.Name == "datasets", ct);
        var now = DateTime.UtcNow;
        DatasetSnapshot? snapshot = null;
        try
        {
            if (nas.DatasetsJson is { } json)
                snapshot = JsonSerializer.Deserialize<DatasetSnapshot>(json);
        }
        catch (JsonException)
        {
        }

        var validUntilUtc = capability?.LastSuccessUtc?.AddSeconds(3L * nas.PollSeconds);
        await Send.ResultAsync(Results.Ok(new Response(settings.ReadDatasets, !settings.ReadDatasets ? "disabled" : !nas.Enabled ? "paused" : capability?.EffectiveStatus(now) ?? "unavailable", validUntilUtc, snapshot)));
        return;
    }
}
