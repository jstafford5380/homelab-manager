using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public static class PoolIntegrity
{
    // Independent of the pool's ONLINE/DEGRADED state and scan error count.
    // A later OK code cannot prove damaged files were recovered.
    public static async Task RecordAsync(HomelabDb db, PoolSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.StatusCode is not ("CORRUPT_DATA" or "CORRUPT_POOL")) return;
        var rule = "pool-integrity-v1:" + snapshot.PoolGuid;
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.ConnectionId == snapshot.ConnectionId &&
            x.Rule == rule && x.State != "resolved", ct);
        if (finding is null) {
            finding = new Finding { ConnectionId = snapshot.ConnectionId, Rule = rule,
                FirstSeenUtc = snapshot.FetchedUtc };
            db.Findings.Add(finding);
        }
        finding.Severity = "critical";
        finding.Title = "Pool " + snapshot.Name + " reports " +
            (snapshot.StatusCode == "CORRUPT_DATA" ? "data corruption" : "metadata corruption");
        finding.Evidence = $"TrueNAS pool.query reported {snapshot.StatusCode} for pool GUID {snapshot.PoolGuid} " +
            $"with state {snapshot.Status} at {snapshot.FetchedUtc:O}. This does not identify an affected file or failed drive.";
        finding.NextStep = "Inspect native TrueNAS error details, verify independent recovery copies, and follow your incident runbook. " +
            "Do not clear errors or remove a drive based on this finding. Later ONLINE/OK readings do not prove recovery; review and document resolution.";
        finding.LastSeenUtc = snapshot.FetchedUtc;
    }
}
