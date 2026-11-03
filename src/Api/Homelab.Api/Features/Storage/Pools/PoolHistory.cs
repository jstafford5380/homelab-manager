using System.Text.Json;

namespace Homelab.Api;
public static class PoolHistory
{
    // Contract: TrueNAS middleware release/24.10.2 pool_/pool.py ENTRY.
    // A fetch is evidence of reported state, not a new scan event or its completion time.
    public static PoolSnapshot? Normalize(Guid connection, JsonElement row, DateTime fetched)
    {
        var snapshot = Parse(connection, row);
        if (snapshot is not null)
            SetFetchTime(snapshot, fetched);
        return snapshot;
    }

    internal static PoolSnapshot? Parse(Guid connection, JsonElement row)
    {
        var guid = Text(row, "guid");
        if (string.IsNullOrWhiteSpace(guid))
            return null; // Never merge unnamed pools.
        var result = new PoolSnapshot
        {
            ConnectionId = connection,
            PoolGuid = guid,
            Name = Text(row, "name") ?? "",
            Status = Text(row, "status") ?? "unknown",
            StatusCode = StatusCode(row),
            TopologySignature = PoolCapacity.TopologySignature(row),
            VdevEvidenceJson = VdevEvidence.Capture(row),
            SizeBytes = Count(row, "size"),
            AllocatedBytes = Count(row, "allocated"),
            FreeBytes = Count(row, "free")
        };
        if (row.TryGetProperty("scan", out var scan) && scan.ValueKind == JsonValueKind.Object)
        {
            result.ScanFunction = Text(scan, "function");
            result.ScanState = Text(scan, "state");
            result.ScanErrors = Count(scan, "errors");
            result.ScanStartedUtc = SourceTime(scan, "start_time");
            var end = SourceTime(scan, "end_time");
            var pause = SourceTime(scan, "pause");
            // Do not manufacture an event time from polling or retain contradictory timing.
            if (result.ScanState is "FINISHED" or "CANCELED" && end >= result.ScanStartedUtc)
                result.ScanEndedUtc = end;
            if (result.ScanState is "SCANNING" or "PAUSED")
            {
                if (pause >= result.ScanStartedUtc)
                    result.ScanPausedUtc = pause;
                var remaining = Count(scan, "total_secs_left");
                if (remaining <= 9007199254740991L)
                    result.ScanRemainingSeconds = remaining;
            }

            if (scan.TryGetProperty("percentage", out var percentage) && percentage.ValueKind == JsonValueKind.Number && percentage.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 && value <= 100)
                result.ScanPercentage = value;
        }

        return result;
    }

    internal static void SetFetchTime(PoolSnapshot snapshot, DateTime fetched)
    {
        snapshot.FetchedUtc = fetched;
        if (snapshot.ScanStartedUtc > fetched)
        {
            snapshot.ScanStartedUtc = null;
            snapshot.ScanEndedUtc = null;
            snapshot.ScanPausedUtc = null;
        }
        if (snapshot.ScanEndedUtc > fetched)
            snapshot.ScanEndedUtc = null;
        if (snapshot.ScanPausedUtc > fetched)
            snapshot.ScanPausedUtc = null;
    }

    public static string? StatusCode(JsonElement row)
    {
        var code = Text(row, "status_code");
        return code is { Length: > 0 and <= 64 } && code.All(c => c is >= 'A' and <= 'Z' or '_') ? code : null;
    }

    // TrueNAS EJSON datetime: one $date property containing epoch milliseconds.
    private static DateTime? SourceTime(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 || !value.TryGetProperty("$date", out var date) || date.ValueKind != JsonValueKind.Number || !date.TryGetInt64(out var milliseconds) || milliseconds <= 0 || milliseconds > 253402300799999L)
            return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
    }

    private static string? Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? Count(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count >= 0 ? count : null;
}
