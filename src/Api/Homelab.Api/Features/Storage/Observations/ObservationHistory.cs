using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class ObservationHistory
{
    public static async Task<ObservationPage> ReadAsync(HomelabDb db, Guid diskId, string metric, int days, DateTime asOfUtc, long? snapshotId = null, long? before = null, CancellationToken ct = default)
    {
        var rows = db.Observations.AsNoTracking().Where(x => x.DiskId == diskId && x.Metric == metric);
        var ceiling = snapshotId ?? await rows.MaxAsync(x => (long? )x.Id, ct) ?? 0;
        rows = rows.Where(x => x.Id <= ceiling);
        if (before is { } cursor)
            rows = rows.Where(x => x.Id < cursor);
        // Undated evidence remains visible, but is never assigned to a time window or plotted.
        rows = rows.Where(x => x.ObservedUtc == null || x.ObservedUtc <= asOfUtc);
        if (days != 0)
        {
            var since = asOfUtc.AddDays(-days);
            rows = rows.Where(x => x.ObservedUtc == null || x.ObservedUtc >= since);
        }

        var batch = await rows.OrderByDescending(x => x.Id).Take(101).ToArrayAsync(ct);
        var items = batch.Take(100).ToArray();
        return new(asOfUtc, ceiling, items, batch.Length > 100 ? batch[99].Id : null, await ObservationChanges.ReadAsync(db, diskId, metric, ceiling, items, asOfUtc, ct));
    }
}
