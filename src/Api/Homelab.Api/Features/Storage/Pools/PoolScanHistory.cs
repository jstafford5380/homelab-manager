using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class PoolScanHistory
{
    // Caller owns the writer lease and collection transaction.
    public static async Task RecordAsync(HomelabDb db, PoolSnapshot row, CancellationToken ct = default)
    {
        if (row.ScanStartedUtc is not { } start || start <= DateTime.UnixEpoch || start > row.FetchedUtc || string.IsNullOrWhiteSpace(row.PoolGuid) || row.ScanFunction is not ("SCRUB" or "RESILVER") || row.ScanState is not ("SCANNING" or "PAUSED" or "FINISHED" or "CANCELED"))
            return;
        var scan = db.PoolScans.Local.SingleOrDefault(x => x.ConnectionId == row.ConnectionId && x.PoolGuid == row.PoolGuid && x.ScanFunction == row.ScanFunction && x.ScanStartedUtc == start) ?? await db.PoolScans.SingleOrDefaultAsync(x => x.ConnectionId == row.ConnectionId && x.PoolGuid == row.PoolGuid && x.ScanFunction == row.ScanFunction && x.ScanStartedUtc == start, ct);
        if (scan is null)
        {
            scan = new PoolScan
            {
                ConnectionId = row.ConnectionId,
                PoolGuid = row.PoolGuid,
                ScanFunction = row.ScanFunction,
                ScanStartedUtc = start,
                FirstFetchedUtc = row.FetchedUtc
            };
            db.PoolScans.Add(scan);
        }
        else
        {
            if (row.FetchedUtc < scan.FirstFetchedUtc)
                scan.FirstFetchedUtc = row.FetchedUtc;
        }

        var previousPeak = scan.ScanErrorsPeak;
        var errors = row.ScanErrors is >= 0 and <= 9007199254740991L ? row.ScanErrors : null;
        if (errors is { } count && (scan.ScanErrorsPeak is null || count > scan.ScanErrorsPeak))
            scan.ScanErrorsPeak = count;
        if (row.FetchedUtc <= scan.LastFetchedUtc)
            return; // Backfill may retain a peak, never replay a finding.
        scan.LastFetchedUtc = row.FetchedUtc;
        scan.SourceSchema = row.SourceSchema;
        scan.ScanState = row.ScanState;
        scan.ScanEndedUtc = row.ScanState is "FINISHED" or "CANCELED" && row.ScanEndedUtc >= start && row.ScanEndedUtc <= row.FetchedUtc ? row.ScanEndedUtc : null;
        scan.ScanPausedUtc = row.ScanState is "SCANNING" or "PAUSED" && row.ScanPausedUtc >= start && row.ScanPausedUtc <= row.FetchedUtc ? row.ScanPausedUtc : null;
        scan.ScanRemainingSeconds = row.ScanState is "SCANNING" or "PAUSED" && row.ScanRemainingSeconds is >= 0 and <= 9007199254740991L ? row.ScanRemainingSeconds : null;
        scan.ScanPercentage = row.ScanPercentage is >= 0 and <= 100 ? row.ScanPercentage : null;
        scan.ScanErrors = errors;
        if (errors is not > 0)
            return;
        var rule = $"scan-errors-v1:{row.PoolGuid}:{row.ScanFunction}:{start.Ticks}";
        var finding = db.Findings.Local.SingleOrDefault(x => x.ConnectionId == row.ConnectionId && x.Rule == rule && x.State != "resolved") ?? await db.Findings.FirstOrDefaultAsync(x => x.ConnectionId == row.ConnectionId && x.Rule == rule && x.State != "resolved", ct);
        if (finding is null)
        {
            // Stable reports of the same scan must not recreate a manually reviewed episode.
            if (previousPeak >= errors && await db.Findings.AnyAsync(x => x.ConnectionId == row.ConnectionId && x.Rule == rule, ct))
                return;
            finding = new Finding
            {
                ConnectionId = row.ConnectionId,
                Rule = rule,
                FirstSeenUtc = row.FetchedUtc
            };
            db.Findings.Add(finding);
        }

        finding.Severity = "urgent";
        finding.Title = "Pool " + row.Name + " scan reported errors";
        finding.Evidence = $"{row.ScanFunction} starting {start:O} for pool GUID {row.PoolGuid} reported {errors} scan errors at {row.FetchedUtc:O}; state {row.ScanState}. Highest retained reported count: {scan.ScanErrorsPeak}.";
        finding.NextStep = "Review native scan/error details, independent recovery copies and your incident runbook. Scan errors alone do not identify a failed drive or establish permanent data loss. A later zero, missing count or finished state does not prove repair; document resolution after review.";
        finding.LastSeenUtc = row.FetchedUtc;
    }
}
