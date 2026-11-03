using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class CapacityOutlook
{
    public const int BasisDays = 30;
    public const int MaximumSamples = 300000;
    public static PoolCapacityOutlook FromEstimate(Guid connectionId, string poolGuid, string poolName, CapacityEstimate estimate)
    {
        // The ETA is based on the last measurement, not the time a browser opens overview.
        DateTime? projected = estimate.Status == "estimated" && estimate.DaysTo80Percent is >= 0 and <= 3650 && estimate.LastUtc is { } last ? last.AddDays(estimate.DaysTo80Percent.Value) : null;
        double? used = estimate.SizeBytes is> 0 && estimate.AllocatedBytes is >= 0 && estimate.AllocatedBytes <= estimate.SizeBytes ? (double)estimate.AllocatedBytes.Value / estimate.SizeBytes.Value : null;
        return new(connectionId, poolGuid, poolName, used, .8, projected, estimate.WindowDays, projected is null ? "unavailable" : "supported-trend", estimate.Status, estimate.Explanation, estimate.LastUtc, estimate.FirstUtc, estimate.Days, estimate.Fit);
    }

    public static async Task<PoolCapacityOutlook[]> ReadAsync(HomelabDb db, DateTime now, CancellationToken ct = default)
    {
        var latest = await db.PoolSnapshots.AsNoTracking().GroupBy(x => new { x.ConnectionId, x.PoolGuid }).Select(g => g.OrderByDescending(x => x.FetchedUtc).ThenByDescending(x => x.Id).First()).ToListAsync(ct);
        if (latest.Count == 0)
            return[];
        var connections = await db.Connections.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var capabilities = await db.Capabilities.AsNoTracking().Where(x => x.Name == "pools").ToDictionaryAsync(x => x.ConnectionId, ct);
        var windows = latest.Where(x => connections.ContainsKey(x.ConnectionId)).ToDictionary(x => (x.ConnectionId, x.PoolGuid), x => new PoolCapacity.Window(BasisDays, now, TimeSpan.FromSeconds(Math.Max(300, connections[x.ConnectionId].PollSeconds * 3))));
        var count = 0;
        // Four fixed database reads, independent of pool count. Project only forecast fields;
        // large topology/scan JSON is not loaded for every historical sample.
        await foreach (var row in db.PoolSnapshots.AsNoTracking().Where(x => x.FetchedUtc >= now.AddDays(-BasisDays) && x.FetchedUtc <= now).OrderBy(x => x.FetchedUtc).ThenBy(x => x.Id).Take(MaximumSamples + 1).Select(x => new PoolSnapshot { ConnectionId = x.ConnectionId, PoolGuid = x.PoolGuid, FetchedUtc = x.FetchedUtc, SizeBytes = x.SizeBytes, AllocatedBytes = x.AllocatedBytes, Status = x.Status, TopologySignature = x.TopologySignature, SourceSchema = x.SourceSchema }).AsAsyncEnumerable().WithCancellation(ct))
        {
            count++;
            if (windows.TryGetValue((row.ConnectionId, row.PoolGuid), out var window))
                window.Add(row);
        }

        return latest.Where(x => windows.ContainsKey((x.ConnectionId, x.PoolGuid))).Select(row =>
        {
            var estimate = windows[(row.ConnectionId, row.PoolGuid)].Finish();
            if (count > MaximumSamples)
                estimate = estimate with
                {
                    Status = "suppressed",
                    DaysTo80Percent = null,
                    Explanation = "The overview history limit was reached. No projection is shown from a truncated history; review the individual pool capacity endpoint."
                };
            capabilities.TryGetValue(row.ConnectionId, out var capability);
            estimate = PoolCapacity.RequireCurrentContext(estimate, connections[row.ConnectionId], capability, row.PoolGuid, now);
            if (row.FetchedUtc > now)
                estimate = estimate with
                {
                    Status = "unverified",
                    DaysTo80Percent = null,
                    Explanation = "The latest pool sample has a future timestamp. No current projection can be verified."
                };
            return FromEstimate(row.ConnectionId, row.PoolGuid, row.Name, estimate);
        }).OrderBy(x => x.ConnectionId).ThenBy(x => x.PoolName, StringComparer.Ordinal).ToArray();
    }
}
