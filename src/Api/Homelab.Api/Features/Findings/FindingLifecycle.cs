using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Homelab.Api;
public static class FindingLifecycle
{
    public static string ResolutionToken(Finding finding) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(finding))));
    public static bool ExpireControls(Finding finding, DateTime now)
    {
        if (finding.State == "resolved")
            return false;
        var expired = false;
        if (finding.State == "acknowledged" && finding.ReviewUtc <= now)
        {
            finding.State = "open";
            finding.ReviewUtc = null;
            expired = true;
        }

        if (finding.SnoozedUntilUtc <= now)
        {
            finding.SnoozedUntilUtc = null;
            expired = true;
        }

        if (expired)
            finding.ReminderSequence++;
        return expired;
    }

    public static bool CanResolvePresence(Finding finding, Observation current, Observation? previous, DateTime now, TimeSpan maxAge)
    {
        return finding.State != "resolved" && current.Metric is "ata.pending_sectors" or "ata.offline_uncorrectable" or "nvme.critical_warning" && finding.Rule == (current.Metric == "nvme.critical_warning" ? "nvme-warning-v1" : "media-present-v1:" + current.Metric) && current.Value == 0 && previous?.Value == 0 && current.Quality == "observed" && previous.Quality == "observed" && current.ObservedUtc <= now && now - previous.ObservedUtc <= maxAge && current.ObservedUtc > previous.ObservedUtc && previous.ObservedUtc > finding.EvidenceObservedUtc && finding.EvidenceUnit is not null && current.Unit == finding.EvidenceUnit && previous.Unit == current.Unit && current.Source == finding.EvidenceSource && previous.Source == current.Source && current.ResetEpoch == finding.EvidenceEpoch && current.SourceSchema == finding.EvidenceSchema && current.ResetEpoch.Length > 0 && current.ResetEpoch == previous.ResetEpoch && current.SourceSchema == previous.SourceSchema;
    }

    public static void Resolve(Finding finding, DateTime now, string evidence)
    {
        finding.State = "resolved";
        finding.ResolvedUtc = now;
        finding.ResolutionEvidence = evidence;
        finding.SnoozedUntilUtc = null;
        finding.ReviewUtc = null;
    }

    public static async Task ExpireAsync(HomelabDb db, DateTime now, CancellationToken ct)
    {
        var active = await db.Findings.Where(f => f.State != "resolved" && (f.ReviewUtc <= now || f.SnoozedUntilUtc <= now)).ToListAsync(ct);
        foreach (var finding in active)
            if (ExpireControls(finding, now))
                db.Activity.Add(new() { Action = "Finding review/snooze expired", Detail = finding.Id.ToString() });
        await db.SaveChangesAsync(ct);
    }
}
