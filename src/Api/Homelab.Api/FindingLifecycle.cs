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
        if (finding.State == "resolved") return false;
        var expired = false;
        if (finding.State == "acknowledged" && finding.ReviewUtc <= now) { finding.State = "open"; finding.ReviewUtc = null; expired = true; }
        if (finding.SnoozedUntilUtc <= now) { finding.SnoozedUntilUtc = null; expired = true; }
        if (expired) finding.ReminderSequence++;
        return expired;
    }
    public static bool CanResolvePresence(Finding finding, Observation current, Observation? previous, DateTime now, TimeSpan maxAge)
    {
        return finding.State != "resolved" && current.Metric is "ata.pending_sectors" or "ata.offline_uncorrectable" or "nvme.critical_warning"
            && finding.Rule == (current.Metric == "nvme.critical_warning" ? "nvme-warning-v1" : "media-present-v1:" + current.Metric)
            && current.Value == 0 && previous?.Value == 0 && current.Quality == "observed" && previous.Quality == "observed"
            && current.ObservedUtc <= now && now - previous.ObservedUtc <= maxAge
            && current.ObservedUtc > previous.ObservedUtc && previous.ObservedUtc > finding.EvidenceObservedUtc
            && finding.EvidenceUnit is not null && current.Unit == finding.EvidenceUnit && previous.Unit == current.Unit
            && current.Source == finding.EvidenceSource && previous.Source == current.Source
            && current.ResetEpoch == finding.EvidenceEpoch && current.SourceSchema == finding.EvidenceSchema
            && current.ResetEpoch.Length > 0 && current.ResetEpoch == previous.ResetEpoch && current.SourceSchema == previous.SourceSchema;
    }
    public static void Resolve(Finding finding, DateTime now, string evidence)
    {
        finding.State = "resolved"; finding.ResolvedUtc = now; finding.ResolutionEvidence = evidence;
        finding.SnoozedUntilUtc = null; finding.ReviewUtc = null;
    }
    public static async Task ExpireAsync(HomelabDb db, DateTime now, CancellationToken ct)
    {
        var active = await db.Findings.Where(f => f.State != "resolved" && (f.ReviewUtc <= now || f.SnoozedUntilUtc <= now)).ToListAsync(ct);
        foreach (var finding in active) if (ExpireControls(finding, now)) db.Activity.Add(new() { Action = "Finding review/snooze expired", Detail = finding.Id.ToString() });
        await db.SaveChangesAsync(ct);
    }
    public static void MapFindings(this RouteGroupBuilder api)
    {
        api.MapPost("/findings/{id:guid}/resolve", async (Guid id, FindingResolutionInput input, HomelabDb db, HttpContext ctx) => {
            var finding = await db.Findings.FindAsync(id); if (finding is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000 || !input.ReviewedCurrentEvidence)
                return Results.BadRequest(new { error = "Record resolution evidence (up to 4000 characters) and confirm you reviewed current evidence and recovery readiness." });
            if (finding.State == "resolved") return Results.Conflict(new { error = "This finding is already resolved. Refresh to read the saved resolution." });
            if (input.ResolutionToken != ResolutionToken(finding)) return Results.Conflict(new { error = "The finding changed after you opened the form. Close it, refresh the finding and review the latest evidence before resolving it." });
            Resolve(finding, DateTime.UtcNow, input.Evidence);
            db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Finding manually resolved", Detail = JsonSerializer.Serialize(new { finding.Id, finding.Rule, finding.ResolutionEvidence, finding.ResolvedUtc }) });
            await db.SaveChangesAsync(); return Results.Ok(finding);
        });
        api.MapPost("/findings/{id:guid}/snooze", async (Guid id, SnoozeInput input, HomelabDb db, HttpContext ctx) => {
            if (input.UntilUtc <= DateTime.UtcNow || input.UntilUtc > DateTime.UtcNow.AddDays(7)) return Results.BadRequest(new { error = "Snooze must end within seven days." });
            var finding = await db.Findings.FindAsync(id); if (finding is null) return Results.NotFound();
            if (finding.State == "resolved") return Results.Conflict(new { error = "Resolved findings cannot be snoozed." });
            finding.SnoozedUntilUtc = input.UntilUtc; finding.AssignedOwner = ctx.User.Identity!.Name;
            db.Activity.Add(new() { Actor = ctx.User.Identity.Name!, Action = "Finding snoozed", Detail = id + " until " + input.UntilUtc.ToString("O") }); await db.SaveChangesAsync(); return Results.Ok();
        });
        api.MapPost("/findings/{id:guid}/unsnooze", async (Guid id, HomelabDb db, HttpContext ctx) => {
            var finding = await db.Findings.FindAsync(id); if (finding is null) return Results.NotFound();
            finding.SnoozedUntilUtc = null; finding.ReminderSequence++;
            db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Finding snooze cleared", Detail = id.ToString() }); await db.SaveChangesAsync(); return Results.Ok();
        });
        api.MapPost("/notifications/deliveries/{id:guid}/retry", async (Guid id, HomelabDb db) => {
            var delivery = await db.Deliveries.FindAsync(id); if (delivery is null) return Results.NotFound();
            var channel = await db.NotificationChannels.SingleOrDefaultAsync();
            if (delivery.Status != "failed" || channel?.Enabled != true || channel.TestedUtc is null || delivery.ChannelRevision != channel.Revision) return Results.Conflict(new { error = "Only failed deliveries for the current tested, enabled channel can be retried." });
            delivery.Status = "pending"; delivery.Attempts = 0; delivery.NextAttemptUtc = DateTime.UtcNow;
            db.Activity.Add(new() { Action = "Notification retry requested", Detail = id.ToString() }); await db.SaveChangesAsync(); return Results.Ok();
        });
    }
}
public record SnoozeInput(DateTime UntilUtc);

public record FindingResolutionInput(string ResolutionToken, string Evidence, bool ReviewedCurrentEvidence);
