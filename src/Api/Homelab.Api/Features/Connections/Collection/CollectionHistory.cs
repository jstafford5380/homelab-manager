using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class CollectionHistory
{
    // Project only recorded capability states, without per-attempt success timestamps in the
    // grouping key. These are collection diagnostics, never an availability percentage.
    public static CollectionCoverage Coverage(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 65536)
            return new(false, []);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength()is 0 or > 32)
                return new(false, []);
            var items = new List<CollectionCapability>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("Name", out var name) || name.ValueKind != JsonValueKind.String || !entry.TryGetProperty("Status", out var status) || status.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 100 || string.IsNullOrWhiteSpace(status.GetString()) || status.GetString()!.Length > 100)
                    return new(false, []);
                int? seconds = entry.TryGetProperty("ExpectedSeconds", out var cadence) && cadence.ValueKind == JsonValueKind.Number && cadence.TryGetInt32(out var n) && n > 0 ? n : null;
                items.Add(new(name.GetString()!, status.GetString()!, seconds));
            }

            if (items.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != items.Count)
                return new(false, []);
            return new(true, items.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray());
        }
        catch (JsonException)
        {
            return new(false, []);
        }
    }

    public static double? Duration(CollectionRun run) => run.EndedUtc >= run.StartedUtc && run.StartedUtc >= DateTime.UnixEpoch ? (run.EndedUtc - run.StartedUtc).TotalMilliseconds : null;
    // Caller owns the writer lease and has completed a pre-cleanup backup.
    public static async Task<int> CompactAsync(HomelabDb db, DateTime now, int days, CancellationToken ct = default)
    {
        var cutoff = PoolRetention.DetailCutoff(now, days);
        var summaryCutoff = PoolRetention.SummaryCutoff(now);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.CollectionRuns.Where(x => x.StartedUtc < cutoff).OrderBy(x => x.StartedUtc).ThenBy(x => x.Id).Take(PoolRetention.BatchSize).ToListAsync(ct);
        var projected = rows.Where(x => x.StartedUtc >= summaryCutoff).Select(row =>
        {
            var coverage = Coverage(row.CapabilitySummaryJson);
            var json = JsonSerializer.Serialize(coverage.Items);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { row.ConnectionId, Day = row.StartedUtc.Date, row.Result, coverage.Known, Capabilities = json }))));
            return new
            {
                Row = row,
                Coverage = coverage,
                Json = json,
                Key = key
            };
        });
        foreach (var group in projected.GroupBy(x => x.Key))
        {
            var first = group.First();
            var summary = await db.CollectionDailySummaries.SingleOrDefaultAsync(x => x.GroupKey == group.Key, ct);
            if (summary is null)
            {
                summary = new()
                {
                    GroupKey = group.Key,
                    ConnectionId = first.Row.ConnectionId,
                    DayUtc = first.Row.StartedUtc.Date,
                    Result = first.Row.Result,
                    CapabilitiesKnown = first.Coverage.Known,
                    CapabilitiesJson = first.Json,
                    FirstStartedUtc = first.Row.StartedUtc,
                    LastStartedUtc = first.Row.StartedUtc
                };
                db.CollectionDailySummaries.Add(summary);
            }

            foreach (var entry in group)
            {
                summary.AttemptCount++;
                if (entry.Row.StartedUtc < summary.FirstStartedUtc)
                    summary.FirstStartedUtc = entry.Row.StartedUtc;
                if (entry.Row.StartedUtc > summary.LastStartedUtc)
                    summary.LastStartedUtc = entry.Row.StartedUtc;
                if (Duration(entry.Row)is { } duration)
                {
                    summary.DurationKnownCount++;
                    summary.DurationMinMs = summary.DurationMinMs is { } min ? Math.Min(min, duration) : duration;
                    summary.DurationMaxMs = summary.DurationMaxMs is { } max ? Math.Max(max, duration) : duration;
                }
            }
        }

        db.CollectionRuns.RemoveRange(rows);
        var expired = await db.CollectionDailySummaries.Where(x => x.DayUtc < summaryCutoff).OrderBy(x => x.Id).Take(PoolRetention.BatchSize).ToListAsync(ct);
        db.CollectionDailySummaries.RemoveRange(expired);
        if (rows.Count + expired.Count > 0)
            db.Activity.Add(new() { Action = "Collection history retention completed", Detail = $"{rows.Count} attempts compacted or expired; {expired.Count} daily groups expired. Counts describe saved attempts, not NAS uptime." });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return rows.Count + expired.Count;
    }
}
