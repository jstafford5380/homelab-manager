using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class ScanOverview
{
    public static async Task<ScanReport[]> ReadAsync(HomelabDb db, DateTime now, CancellationToken ct = default)
    {
        // Choose each pool's newest observation BEFORE filtering scan state: a completed
        // or missing scan report must supersede an older SCANNING observation.
        var latest = await db.PoolSnapshots.AsNoTracking().GroupBy(x => new { x.ConnectionId, x.PoolGuid }).Select(g => g.OrderByDescending(x => x.FetchedUtc).ThenByDescending(x => x.Id).First()).ToListAsync(ct);
        var connections = await db.Connections.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var capabilities = await db.Capabilities.AsNoTracking().Where(x => x.Name == "pools").ToListAsync(ct);
        return latest.Where(x => x.ScanState is "SCANNING" or "PAUSED").Select(row =>
        {
            connections.TryGetValue(row.ConnectionId, out var nas);
            var capability = capabilities.SingleOrDefault(x => x.ConnectionId == row.ConnectionId);
            var until = row.FetchedUtc.AddSeconds(Math.Clamp(capability?.ExpectedSeconds ?? 60, 1, 86400) * 3);
            var freshness = nas is null || !nas.Enabled ? "paused" : capability?.EffectiveStatus(now) != "supported" ? capability?.EffectiveStatus(now) ?? "unknown" : row.FetchedUtc > now || !PoolEvidence.ContainsPool(nas.TopologyJson, row.PoolGuid) ? "unverified" : until < now ? "stale" : "fresh";
            var explanation = freshness == "fresh" ? "Latest pool read reported this scan state. Progress is source-reported; no completion time is inferred." : "Last-known scan report only. Its current operation state is not verified; check pool coverage and native details.";
            return new ScanReport(row.ConnectionId, nas?.Name ?? "Unavailable NAS", row.PoolGuid, row.Name, row.ScanFunction ?? "Unknown scan", row.ScanState!, row.FetchedUtc, freshness == "fresh" ? until : null, freshness, explanation, row.ScanPercentage is >= 0 and <= 100 ? row.ScanPercentage : null, row.ScanErrors is >= 0 and <= 9007199254740991L ? row.ScanErrors : null, row.ScanStartedUtc, row.ScanEndedUtc, row.ScanPausedUtc, row.ScanRemainingSeconds);
        }).OrderBy(x => x.NasName).ThenBy(x => x.PoolName).ToArray();
    }
}
