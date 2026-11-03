using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class NotificationEngine(IDataProtectionProvider protection, IWebhookSender sender, WriteCoordinator writes)
{
    internal Task<int> SendAsync(string kind, string encryptedDestination, string payload, CancellationToken ct)
    {
        var destination = protection.CreateProtector("webhook-url").Unprotect(encryptedDestination);
        if (kind == "telegram")
        {
            var telegram = JsonSerializer.Deserialize<TelegramDestination>(destination) ?? throw new InvalidDataException();
            return sender.SendAsync(TelegramNotifications.Endpoint(telegram), TelegramNotifications.Payload(telegram, payload), ct);
        }

        if (kind != "webhook")
            throw new InvalidDataException("Unsupported notification channel.");
        return sender.SendAsync(new Uri(destination), payload, ct);
    }

    public async Task QueueAsync(HomelabDb db, NotificationChannel channel, DateTime now, CancellationToken ct)
    {
        if (!channel.Enabled || channel.TestedUtc is null)
            return;
        var findings = await db.Findings.ToListAsync(ct);
        foreach (var finding in findings)
        {
            var kind = finding.State == "resolved" ? "resolved" : "opened";
            var prior = await db.Deliveries.Where(d => d.FindingId == finding.Id && d.ChannelRevision == channel.Revision).OrderByDescending(d => d.CreatedUtc).FirstOrDefaultAsync(ct);
            if (kind == "resolved" && prior is null)
                continue;
            if (finding.SnoozedUntilUtc > now && finding.Severity != "critical")
                continue;
            var eventKey = $"{channel.Revision:N}:{finding.Id:N}:{kind}:{finding.Severity}:{finding.ReminderSequence}";
            if (await db.Deliveries.AnyAsync(d => d.EventKey == eventKey, ct))
                continue;
            if (channel.MaintenanceUntilUtc > now && finding.Severity != "critical")
                continue;
            var nas = await db.Connections.FindAsync([finding.ConnectionId], ct);
            var disk = finding.DiskId is { } id ? await db.Disks.FindAsync([id], ct) : null;
            var coverage = await db.Capabilities.Where(c => c.ConnectionId == finding.ConnectionId).ToListAsync(ct);
            db.Deliveries.Add(new() { FindingId = finding.Id, ChannelRevision = channel.Revision, EventKey = eventKey, Kind = prior is null ? "opened" : kind == "resolved" ? "resolved" : finding.ReminderSequence > 0 ? "reminder" : "escalated", PayloadJson = JsonSerializer.Serialize(new { eventId = eventKey, findingId = finding.Id, severity = finding.Severity, state = finding.State, title = channel.MaskIdentifiers ? "NAS observation requires review" : finding.Title, nas = channel.MaskIdentifiers ? null : nas?.Name, serial = channel.MaskIdentifiers ? null : disk?.Serial, bay = channel.MaskIdentifiers || disk?.BayVerified != true || disk.MappingConfidence == "ambiguous" ? "unverified or masked" : disk.Bay, finding.FirstSeenUtc, finding.LastSeenUtc, evidence = channel.MaskIdentifiers ? "Open the application for private evidence." : finding.Evidence, nextStep = channel.MaskIdentifiers ? "Review evidence and recovery readiness in Homelab Control." : finding.NextStep, coverage = coverage.Select(c => new { c.Name, status = c.EffectiveStatus(now), c.LastSuccessUtc }), evidencePath = "/?finding=" + finding.Id, limitation = "Observed condition, not a predicted failure date. Channel acceptance does not prove the notification was read." }) });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeliverOneAsync(HomelabDb db, NotificationChannel channel, DateTime now, CancellationToken ct)
    {
        NotificationDelivery? item;
        string protectedUrl, channelKind;
        using (var claim = await writes.EnterAsync(ct))
        {
            await db.Entry(channel).ReloadAsync(ct);
            if (db.Entry(channel).State == EntityState.Detached || !channel.Enabled || channel.TestedUtc is null)
                return;
            item = await db.Deliveries.Where(d => d.Status == "pending" && d.ChannelRevision == channel.Revision && d.NextAttemptUtc <= now).OrderBy(d => d.CreatedUtc).FirstOrDefaultAsync(ct);
            if (item is null)
                return;
            if (item.Attempts >= 5)
            {
                // A process can stop after persisting its last claim but before recording
                // the receiver response. Exhausted claims must not send a sixth request.
                item.Status = "failed";
                db.DeliveryAttempts.Add(new() { DeliveryId = item.Id, Result = "outcome-unknown" });
                await db.SaveChangesAsync(ct);
                return;
            }

            var finding = await db.Findings.FindAsync([item.FindingId], ct);
            if (finding is not null)
                await db.Entry(finding).ReloadAsync(ct);
            if ((channel.MaintenanceUntilUtc > now || finding?.SnoozedUntilUtc > now) && finding?.Severity != "critical")
            {
                item.NextAttemptUtc = now.AddSeconds(30);
                await db.SaveChangesAsync(ct);
                return;
            }

            item.Attempts++;
            // Persist the claim before releasing the writer. A restart retries the same event ID.
            item.NextAttemptUtc = now.AddMinutes(1);
            protectedUrl = channel.ProtectedUrl;
            channelKind = channel.Kind;
            await db.SaveChangesAsync(ct);
        }

        int? status = null;
        string result;
        try
        {
            status = await SendAsync(channelKind, protectedUrl, item.PayloadJson, ct);
            result = status is >= 200 and <= 299 ? "accepted" : "rejected";
        }
        catch (Exception)when (!ct.IsCancellationRequested)
        {
            result = "unavailable";
        }

        using var completion = await writes.EnterAsync(ct);
        await db.Entry(item).ReloadAsync(ct);
        if (db.Entry(item).State == EntityState.Detached)
            return;
        // A settings edit can cancel this claim while the request is in flight. Record what
        // the receiver did, but never revive a cancelled delivery or overwrite new settings.
        if (item.Status == "pending")
        {
            if (result == "accepted")
            {
                item.Status = "accepted";
                item.AcceptedUtc = DateTime.UtcNow;
            }
            else
            {
                item.Status = item.Attempts >= 5 ? "failed" : "pending";
                item.NextAttemptUtc = now.AddSeconds(Math.Min(900, 30 * Math.Pow(2, item.Attempts)));
            }
        }

        db.DeliveryAttempts.Add(new() { DeliveryId = item.Id, HttpStatus = status, Result = result });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IResult> TestChannelAsync(HomelabDb db, CancellationToken ct)
    {
        NotificationChannel channel;
        Guid revision;
        string protectedUrl, channelKind;
        using (var claim = await writes.EnterAsync(ct))
        {
            var current = await db.NotificationChannels.SingleOrDefaultAsync(ct);
            if (current is null)
                return Results.BadRequest(new { error = "Configure a destination first." });
            channel = current;
            revision = channel.Revision;
            protectedUrl = channel.ProtectedUrl;
            channelKind = channel.Kind;
        }

        int status;
        try
        {
            status = await SendAsync(channelKind, protectedUrl, "{\"type\":\"homelab-test\",\"message\":\"Homelab Control notification test\"}", ct);
        }
        catch (Exception)when (!ct.IsCancellationRequested)
        {
            return Results.BadRequest(new { error = "Test failed. Check hostname enrollment, reachability, and TLS trust." });
        }

        using var completion = await writes.EnterAsync(ct);
        await db.Entry(channel).ReloadAsync(ct);
        db.Activity.Add(new() { Action = "Notification channel test", Detail = "Channel revision " + revision + "; HTTP status " + status });
        if (db.Entry(channel).State == EntityState.Detached || channel.Revision != revision)
        {
            await db.SaveChangesAsync(ct);
            return Results.Conflict(new { error = "The notification destination changed while its test was in progress. Test the current destination before enabling delivery." });
        }

        if (status is >= 200 and <= 299)
            channel.TestedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return status is >= 200 and <= 299 ? Results.Ok() : Results.BadRequest(new { error = "Notifier did not accept the test." });
    }
}
