using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.GetHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/dataset-history");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("StorageDatasetsGetHistory");
            b.WithTags("Connections");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var snapshotId = request.SnapshotId;
        var before = request.Before;
        if (snapshotId < 0 || before <= 0 || before is not null && (snapshotId is null || before > snapshotId))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Use a valid dataset history cursor." }));
            return;
        }

        if (!await db.Connections.AnyAsync(x => x.Id == id, ct))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var ceiling = snapshotId ?? await db.DatasetSamples.Where(x => x.ConnectionId == id).MaxAsync(x => (long? )x.Id, ct) ?? 0;
        var rows = await db.DatasetSamples.AsNoTracking().Where(x => x.ConnectionId == id && x.Id <= ceiling && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(11).ToArrayAsync(ct);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new OperatingSettings();
        await Send.ResultAsync(Results.Ok(new Response(ceiling, rows.Take(10).Select(x => new ResponseItemsItem(x.Id, x.ConfigurationRevision, x.FetchedUtc, x.SystemSerial, x.Version, JsonSerializer.Deserialize<DatasetSnapshot>(x.SnapshotJson))), rows.Length > 10 ? (long? )rows[9].Id : null, settings.SampleRetentionDays, "First successful inventory per UTC hour and connection revision. Dataset names do not establish identity or rename continuity. Missing samples are gaps, not evidence of deletion.")));
        return;
    }
}
