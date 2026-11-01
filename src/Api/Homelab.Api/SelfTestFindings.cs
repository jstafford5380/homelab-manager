using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

// A fingerprint identifies repeated reported evidence, not a physical test event.
public sealed class SelfTestEvidence
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public Guid ReportId { get; set; }
    public string Fingerprint { get; set; } = "";
}
public static class SelfTestFindings
{
    public const string Rule = "ata-self-test-history-v1";
    public static string Fingerprint(AtaSelfTestEntry entry) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { entry.TypeCode, entry.StatusCode, entry.LifetimeHours, entry.FirstErrorLba }))));

    // Caller owns the transaction and writer lease. Records are staged alongside the imported report.
    public static async Task StageAsync(HomelabDb db, Disk disk, SmartReport report, SmartReportPreview preview)
    {
        if (preview.Protocol != "ATA" || preview.ObservedUtc is null || (preview.ExitStatus & 7) != 0) return;
        var failures = (preview.AtaSelfTests ?? []).Where(x => x.Quality == "observed")
            .SelectMany(x => x.Entries).Where(x => x.Result == "failed").GroupBy(Fingerprint).Select(x => x.First()).ToArray();
        if (failures.Length == 0) return; // Success or missing logs never resolve adverse evidence.
        var fingerprints = failures.Select(Fingerprint).ToArray();
        var seen = await db.SelfTestEvidence.Where(x => x.DiskId == disk.Id && fingerprints.Contains(x.Fingerprint)).Select(x => x.Fingerprint).ToListAsync();
        var novel = failures.Where(x => !seen.Contains(Fingerprint(x))).ToArray();
        if (novel.Length == 0) return;
        foreach (var entry in novel) db.SelfTestEvidence.Add(new() { DiskId = disk.Id, ReportId = report.Id, Fingerprint = Fingerprint(entry) });
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.DiskId == disk.Id && x.Rule == Rule && x.State != "resolved");
        var latestReviewed = await db.Findings.Where(x => x.DiskId == disk.Id && x.Rule == Rule && x.State == "resolved")
            .MaxAsync(x => x.EvidenceObservedUtc);
        // Backfill remains stored but cannot reopen an episode reviewed using a newer report.
        if (finding is null && latestReviewed >= preview.ObservedUtc) return;
        if (finding is null) {
            finding = new() { ConnectionId = disk.ConnectionId, DiskId = disk.Id, Rule = Rule, FirstSeenUtc = report.ImportedUtc };
            db.Findings.Add(finding);
        }
        if (finding.EvidenceObservedUtc > preview.ObservedUtc) return;
        finding.Severity = "review";
        finding.Title = "Imported ATA self-test failure needs review";
        finding.Evidence = $"Report {report.Id} from {preview.ObservedUtc:O} contains {failures.Length} distinct failure evidence fingerprint(s), including {novel.Length} not previously retained by this rule. These are historical log entries, not counts of physical test events or newly occurring failures. Device lifetime hours may wrap; the report time is not the test time.";
        finding.NextStep = "Open this drive's imported SMART report history, inspect failed-test status and error locations, and review independent recovery readiness and native drive evidence. Repeated fingerprints are suppressed; changed details warrant review but do not prove a new test or failed drive. A later successful test does not establish repair. Record resolution after investigation.";
        finding.LastSeenUtc = report.ImportedUtc;
        finding.EvidenceObservedUtc = preview.ObservedUtc;
        finding.EvidenceSource = SmartReports.Source;
        finding.EvidenceSchema = preview.SourceSchema;
        db.Activity.Add(new() { Actor = report.Actor, Action = "Imported self-test failure flagged", Detail = $"Disk {disk.Id}; report {report.Id}; historical evidence only" });
    }
}
