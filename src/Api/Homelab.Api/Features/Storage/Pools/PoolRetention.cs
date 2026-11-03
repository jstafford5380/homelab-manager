using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Homelab.Api;
public static class PoolRetention
{
    public const int BatchSize = 2000;
    public static DateTime DetailCutoff(DateTime now, int days) => now.Date.AddDays(-days);
    public static DateTime SummaryCutoff(DateTime now) => now.Date.AddYears(-2);
    // Caller holds the writer lease. Backup must complete before entering this transaction.
    // All rows in a batch are aggregated and deleted atomically; retries cannot double count.
    public static async Task<int> CompactAsync(HomelabDb db, DateTime now, int days, CancellationToken ct = default)
    {
        var cutoff = DetailCutoff(now, days);
        var summaryCutoff = SummaryCutoff(now);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.PoolSnapshots.Where(x => x.FetchedUtc < cutoff).OrderBy(x => x.FetchedUtc).ThenBy(x => x.Id).Take(BatchSize).ToListAsync(ct);
        foreach (var group in rows.Where(x => x.FetchedUtc >= summaryCutoff).GroupBy(Key))
        {
            var summary = await db.PoolDailySummaries.SingleOrDefaultAsync(x => x.GroupKey == group.Key, ct);
            var first = group.First();
            if (summary is null)
            {
                summary = new PoolDailySummary
                {
                    GroupKey = group.Key,
                    ConnectionId = first.ConnectionId,
                    PoolGuid = first.PoolGuid,
                    DayUtc = first.FetchedUtc.Date,
                    SourceSchema = first.SourceSchema,
                    Status = first.Status,
                    StatusCode = first.StatusCode,
                    ScanFunction = first.ScanFunction,
                    ScanState = first.ScanState,
                    FirstFetchedUtc = first.FetchedUtc,
                    LastFetchedUtc = first.FetchedUtc
                };
                db.PoolDailySummaries.Add(summary);
            }

            foreach (var row in group)
            {
                summary.VdevEvidenceJson = VdevDailyEvidence.Add(summary.VdevEvidenceJson, row.VdevEvidenceJson);
                summary.SampleCount++;
                if (row.FetchedUtc < summary.FirstFetchedUtc)
                    summary.FirstFetchedUtc = row.FetchedUtc;
                if (row.FetchedUtc > summary.LastFetchedUtc)
                    summary.LastFetchedUtc = row.FetchedUtc;
                if (row.SizeBytes is { } valueSizeBytes)
                {
                    summary.SizeBytesKnownCount++;
                    summary.SizeBytesMin = summary.SizeBytesMin is { } minSizeBytes ? Math.Min(minSizeBytes, valueSizeBytes) : valueSizeBytes;
                    summary.SizeBytesMax = summary.SizeBytesMax is { } maxSizeBytes ? Math.Max(maxSizeBytes, valueSizeBytes) : valueSizeBytes;
                }

                if (row.AllocatedBytes is { } valueAllocatedBytes)
                {
                    summary.AllocatedBytesKnownCount++;
                    summary.AllocatedBytesMin = summary.AllocatedBytesMin is { } minAllocatedBytes ? Math.Min(minAllocatedBytes, valueAllocatedBytes) : valueAllocatedBytes;
                    summary.AllocatedBytesMax = summary.AllocatedBytesMax is { } maxAllocatedBytes ? Math.Max(maxAllocatedBytes, valueAllocatedBytes) : valueAllocatedBytes;
                }

                if (row.FreeBytes is { } valueFreeBytes)
                {
                    summary.FreeBytesKnownCount++;
                    summary.FreeBytesMin = summary.FreeBytesMin is { } minFreeBytes ? Math.Min(minFreeBytes, valueFreeBytes) : valueFreeBytes;
                    summary.FreeBytesMax = summary.FreeBytesMax is { } maxFreeBytes ? Math.Max(maxFreeBytes, valueFreeBytes) : valueFreeBytes;
                }

                if (row.ScanErrors is { } valueScanErrors)
                {
                    summary.ScanErrorsKnownCount++;
                    summary.ScanErrorsMin = summary.ScanErrorsMin is { } minScanErrors ? Math.Min(minScanErrors, valueScanErrors) : valueScanErrors;
                    summary.ScanErrorsMax = summary.ScanErrorsMax is { } maxScanErrors ? Math.Max(maxScanErrors, valueScanErrors) : valueScanErrors;
                }

                if (row.ScanPercentage is { } valueScanPercentage)
                {
                    summary.ScanPercentageKnownCount++;
                    summary.ScanPercentageMin = summary.ScanPercentageMin is { } minScanPercentage ? Math.Min(minScanPercentage, valueScanPercentage) : valueScanPercentage;
                    summary.ScanPercentageMax = summary.ScanPercentageMax is { } maxScanPercentage ? Math.Max(maxScanPercentage, valueScanPercentage) : valueScanPercentage;
                }
            }
        }

        db.PoolSnapshots.RemoveRange(rows);
        var expired = await db.PoolDailySummaries.Where(x => x.DayUtc < summaryCutoff).OrderBy(x => x.Id).Take(BatchSize).ToListAsync(ct);
        db.PoolDailySummaries.RemoveRange(expired);
        var expiredScans = await db.PoolScans.Where(x => x.LastFetchedUtc < summaryCutoff).OrderBy(x => x.Id).Take(BatchSize).ToListAsync(ct);
        db.PoolScans.RemoveRange(expiredScans);
        if (rows.Count + expired.Count + expiredScans.Count > 0)
            db.Activity.Add(new() { Action = "Pool history retention completed", Detail = $"{rows.Count} detailed snapshots compacted or expired; {expired.Count} daily groups expired; {expiredScans.Count} scan groups expired. UTC days; summaries retained for two years. Manual drive evidence and incidents unchanged." });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return rows.Count + expired.Count + expiredScans.Count;
    }

    private static string Key(PoolSnapshot row)
    {
        // Preserve legacy keys for unknown codes so interrupted pre-upgrade batches
        // continue the existing group. Known codes must never merge with that group.
        var legacy = JsonSerializer.Serialize(new { row.ConnectionId, row.PoolGuid, Day = row.FetchedUtc.Date, row.SourceSchema, row.Status, row.ScanFunction, row.ScanState });
        var identity = row.StatusCode is null ? legacy : JsonSerializer.Serialize(new { Version = 2, Legacy = legacy, row.StatusCode });
        // Separate member/state/alias contexts; aggregate counters with known counts and extrema.
        if (row.VdevEvidenceJson is not null)
            identity = JsonSerializer.Serialize(new { Version = 3, Legacy = identity, VdevContext = VdevEvidence.Context(row.VdevEvidenceJson) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
