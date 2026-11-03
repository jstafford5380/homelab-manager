using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class ActivityHistory
{
    public static async Task<ActivityHistoryPage> ReadAsync(HomelabDb db, long? snapshotId = null, long? before = null, CancellationToken ct = default)
    {
        var ceiling = snapshotId ?? await db.Activity.Select(x => (long? )x.Id).MaxAsync(ct) ?? 0;
        var rows = await db.Activity.AsNoTracking().Where(x => x.Id <= ceiling && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(51).ToArrayAsync(ct);
        var items = rows.Take(50).ToArray();
        return new(ceiling, items, rows.Length > 50 ? items[^1].Id : null);
    }
}
