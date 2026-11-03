using Microsoft.EntityFrameworkCore;
using static Homelab.Api.ObservationHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Observations.GetHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.ObservationPage>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/observation-history");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("StorageObservationsGetHistory");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Homelab.Api.ObservationPage>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var metric = request.Metric;
        var days = request.Days;
        var asOfUtc = request.AsOfUtc;
        var snapshotId = request.SnapshotId;
        var before = request.Before;
        var now = DateTime.UtcNow;
        if (ObservationValidation.CanonicalUnit(metric)is null || days is not (0 or 1 or 7 or 30) || before is <= 0 || snapshotId is < 0 || (before is not null && (snapshotId is null || asOfUtc is null)) || (asOfUtc is { } time && (time.Kind != DateTimeKind.Utc || time < DateTime.UnixEpoch.AddDays(30) || time > now)))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose a supported metric/window and a valid history cursor with its original UTC window and snapshot." }));
            return;
        }

        if (!await db.Disks.AnyAsync(x => x.Id == id, ct))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        await Send.ResultAsync(Results.Ok(await ReadAsync(db, id, metric, days, asOfUtc ?? now, snapshotId, before, ct)));
        return;
    }
}
