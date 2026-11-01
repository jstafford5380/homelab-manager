using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public static class ResilverFindings
{
    public const string RulePrefix = "resilver-drive-evidence-v1:";
    // Only adverse evidence rules qualify: age, warranty, endurance and identity reviews do not.
    public static bool IsAdverse(string rule) => rule is "smart-overall-failed-v1" or "nvme-warning-v1" or "transport-increase-v1" or "scsi-grown-defects-increase-v1" or SelfTestFindings.Rule
        || rule.StartsWith("media-present-v1:", StringComparison.Ordinal)
        || rule.StartsWith("media-increase-v1:", StringComparison.Ordinal)
        || rule.StartsWith("scsi-uncorrected-increase-v1:", StringComparison.Ordinal);

    // Runs inside the collection transaction. Correlates retained evidence; performs no NAS reads.
    public static async Task RecordAsync(HomelabDb db, PoolSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot.ScanFunction != "RESILVER" || snapshot.ScanState != "SCANNING" || snapshot.ScanPausedUtc is not null
            || snapshot.ScanStartedUtc is not {} start || start > snapshot.FetchedUtc || snapshot.VdevEvidenceJson is null) return;
        var tree = VdevEvidence.Read(snapshot.VdevEvidenceJson);
        if (tree.Status != "reported") return;
        var members = tree.Nodes.Where(x => x.Type == "DISK" && x.State == "ONLINE" && x.Role is "data" or "special" or "dedup"
            && x.DriveMapping is { Status: "inferred", DiskId: not null } && !tree.Nodes.Any(child => child.ParentGuid == x.Guid)).ToArray();
        var ids = members.Select(x => x.DriveMapping!.DiskId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return;
        var sources = await db.Findings.Where(x => x.ConnectionId == snapshot.ConnectionId && x.DiskId != null
            && ids.Contains(x.DiskId.Value) && x.State != "resolved").ToListAsync(ct);
        foreach (var source in sources.Where(x => IsAdverse(x.Rule) && (x.EvidenceObservedUtc is null || x.EvidenceObservedUtc <= snapshot.FetchedUtc))) {
            // One episode per scan and source finding. Poll retries and a reviewed resolution
            // cannot recreate it; a new scan or new source episode can warrant a fresh review.
            var rule = $"{RulePrefix}{snapshot.PoolGuid}:{start.Ticks}:{source.Id:N}";
            var finding = db.Findings.Local.FirstOrDefault(x => x.ConnectionId == snapshot.ConnectionId && x.Rule == rule)
                ?? await db.Findings.FirstOrDefaultAsync(x => x.ConnectionId == snapshot.ConnectionId && x.Rule == rule, ct);
            if (finding is not null) continue; // Preserve the original correlation and its evidence age.
            var mapped = members.Where(x => x.DriveMapping!.DiskId == source.DiskId).ToArray();
            var memberText = string.Join(", ", mapped.Take(8).Select(x => $"{x.Guid} ({x.Role})"));
            var sourceTime = source.EvidenceObservedUtc is {} observed ? observed.ToString("O") : "unknown";
            finding = new Finding {
                ConnectionId = snapshot.ConnectionId, DiskId = source.DiskId, Rule = rule, Severity = source.Severity == "critical" ? "critical" : "urgent",
                Title = $"Drive evidence flagged during resilver of {snapshot.Name}",
                FirstSeenUtc = snapshot.FetchedUtc, LastSeenUtc = snapshot.FetchedUtc,
                // The correlation time must never refresh the drive observation's age.
                EvidenceObservedUtc = source.EvidenceObservedUtc,
                EvidenceSource = "TrueNAS pool.query + same-cycle disk.query mapping + retained local finding",
                EvidenceSchema = "resilver-drive-evidence-v1", EvidenceUnit = "contextual review",
                EvidenceEpoch = "Independent scan read and historical drive evidence; not a counter comparison",
                Evidence = $"Pool GUID {snapshot.PoolGuid} reported RESILVER / SCANNING at {snapshot.FetchedUtc:O}, starting {start:O}. "
                    + $"Inferred member mapping: {memberText}. " + (mapped.Length > 8 ? $"Showing 8 of {mapped.Length} mapped leaves. " : "")
                    + $"Original finding {source.Id}; rule {source.Rule}; state {source.State}; drive observation time {sourceTime}. "
                    + $"Retained evidence: {source.Evidence} "
                    + "The unresolved drive evidence may predate this scan; this does not establish simultaneous deterioration, identify the replacement target, or predict resilver success. Mapping is inferred from sequential reads and does not verify a physical bay.",
                NextStep = "Review the original drive evidence, current native scan and topology, and independent backup/restore readiness promptly. Check shared cable, controller and power context before replacement planning. Do not remove a drive, interrupt the scan or clear errors based on this correlation. Scan completion, missing telemetry or later stable counters do not automatically resolve this review."
            };
            db.Findings.Add(finding);
            db.Activity.Add(new() { Action = "Drive evidence correlated with resilver", Detail = $"Finding {finding.Id}. {finding.Evidence}" });
        }
    }
}
