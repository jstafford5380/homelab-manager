using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class RecoveryHistory
{
    public static async Task<RecoveryHistoryPage> ReadAsync(HomelabDb db, Guid policyId, string kind, DateTime asOfUtc, RecoveryHistoryCursor? before = null, CancellationToken ct = default)
    {
        RecoveryHistoryItem[] rows;
        var key = before?.Id.ToString().ToUpperInvariant();
        if (kind == "runs")
        {
            var query = db.BackupEvidence.AsNoTracking().Where(x => x.PolicyId == policyId && x.RecordedUtc <= asOfUtc);
            if (before is not null)
                query = query.Where(x => x.AttemptedUtc < before.AtUtc || x.AttemptedUtc == before.AtUtc && string.Compare(x.Id.ToString().ToUpper(), key) < 0);
            rows = await query.OrderByDescending(x => x.AttemptedUtc).ThenByDescending(x => x.Id.ToString().ToUpper()).Take(51).Select(x => new RecoveryHistoryItem(x.Id, x.PolicyRevision, x.AttemptedUtc, x.RecordedUtc, x.Author, x.Outcome, x.ReviewByUtc, x.RecoveryPointUtc, x.DestinationConfirmed, null, null, null, x.Evidence, x.FailureReason)).ToArrayAsync(ct);
        }
        else
        {
            var query = db.RestoreExercises.AsNoTracking().Where(x => x.PolicyId == policyId && x.RecordedUtc <= asOfUtc);
            if (before is not null)
                query = query.Where(x => x.TestedUtc < before.AtUtc || x.TestedUtc == before.AtUtc && string.Compare(x.Id.ToString().ToUpper(), key) < 0);
            rows = await query.OrderByDescending(x => x.TestedUtc).ThenByDescending(x => x.Id.ToString().ToUpper()).Take(51).Select(x => new RecoveryHistoryItem(x.Id, x.PolicyRevision, x.TestedUtc, x.RecordedUtc, x.Author, x.Result, x.ReviewByUtc, x.RecoveryPointUtc, null, x.Scope, x.Checks, x.DurationMinutes, x.Evidence, null)).ToArrayAsync(ct);
        }

        var items = rows.Take(50).ToArray();
        return new(asOfUtc, kind, items, rows.Length > 50 ? new(items[^1].OccurredUtc, items[^1].Id) : null);
    }
}
