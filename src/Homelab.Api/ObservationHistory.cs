using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public record ObservationPage(DateTime AsOfUtc, long SnapshotId, Observation[] Items, long? NextBefore, Dictionary<long, ObservationChange> Comparisons);
public static class ObservationHistory
{
    public static async Task<ObservationPage> ReadAsync(HomelabDb db, Guid diskId, string metric, int days,
        DateTime asOfUtc, long? snapshotId = null, long? before = null, CancellationToken ct = default)
    {
        var rows = db.Observations.AsNoTracking().Where(x => x.DiskId == diskId && x.Metric == metric);
        var ceiling = snapshotId ?? await rows.MaxAsync(x => (long?)x.Id, ct) ?? 0;
        rows = rows.Where(x => x.Id <= ceiling);
        if (before is {} cursor) rows = rows.Where(x => x.Id < cursor);
        // Undated evidence remains visible, but is never assigned to a time window or plotted.
        rows = rows.Where(x => x.ObservedUtc == null || x.ObservedUtc <= asOfUtc);
        if (days != 0) { var since = asOfUtc.AddDays(-days); rows = rows.Where(x => x.ObservedUtc == null || x.ObservedUtc >= since); }
        var batch = await rows.OrderByDescending(x => x.Id).Take(101).ToArrayAsync(ct);
        var items = batch.Take(100).ToArray();
        return new(asOfUtc, ceiling, items, batch.Length > 100 ? batch[99].Id : null, await ObservationChanges.ReadAsync(db, diskId, metric, ceiling, items, asOfUtc, ct));
    }
    public static void MapObservationHistory(this RouteGroupBuilder api) => api.MapGet("/disks/{id:guid}/observation-history",
        async (Guid id, string metric, int days, DateTime? asOfUtc, long? snapshotId, long? before, HomelabDb db, CancellationToken ct) => {
            var now = DateTime.UtcNow;
            if (ObservationValidation.CanonicalUnit(metric) is null || days is not (0 or 1 or 7 or 30)
                || before is <= 0 || snapshotId is < 0
                || (before is not null && (snapshotId is null || asOfUtc is null))
                || (asOfUtc is {} time && (time.Kind != DateTimeKind.Utc || time < DateTime.UnixEpoch.AddDays(30) || time > now)))
                return Results.BadRequest(new { error = "Choose a supported metric/window and a valid history cursor with its original UTC window and snapshot." });
            if (!await db.Disks.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            return Results.Ok(await ReadAsync(db, id, metric, days, asOfUtc ?? now, snapshotId, before, ct));
        });
}
