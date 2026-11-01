using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public interface IWebhookSender { Task<int> SendAsync(Uri uri, string payload, CancellationToken ct); }
public sealed class WebhookSender(IConfiguration config, IServiceScopeFactory scopes) : IWebhookSender
{
    public static bool IsForbiddenAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        return IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.AddressFamily == AddressFamily.InterNetwork && (bytes[0] == 169 && bytes[1] == 254 || bytes[0] >= 224 || bytes[0] == 0);
    }
    public static bool IsAllowedUrl(Uri uri, IConfiguration config) => uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && config.GetSection("Homelab:WebhookHosts").Get<string[]>()?.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase) == true;
    public async Task<int> SendAsync(Uri uri, string payload, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        ct = deadline.Token;
        var telegram = TelegramNotifications.IsEndpoint(uri);
        if (!telegram && !IsAllowedUrl(uri, config)) throw new InvalidOperationException("Webhook host is not enrolled in server configuration.");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        var nasHosts = (await db.Connections.AsNoTracking().ToListAsync(ct)).SelectMany(n => new[] { new Uri(n.ApiUri).IdnHost, n.ConnectHost }).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToArray();
        var nasAddresses = new HashSet<IPAddress>();
        foreach (var host in nasHosts) foreach (var address in await Dns.GetHostAddressesAsync(host!, ct)) nasAddresses.Add(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        handler.ConnectCallback = async (context, token) => {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
            if (addresses.Length == 0 || addresses.Any(a => IsForbiddenAddress(a) || nasAddresses.Contains(a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a))) throw new InvalidOperationException("Webhook address is not allowed.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        using var suppressTelemetry = OpenTelemetry.SuppressInstrumentationScope.Begin();
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (telegram && response.IsSuccessStatusCode) {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0) {
                if (body.Length + count > 65536) throw new InvalidDataException("Notifier response exceeded its bound.");
                body.Write(buffer, 0, count);
            }
            if (!TelegramNotifications.Accepted(Encoding.UTF8.GetString(body.ToArray()))) throw new InvalidDataException("Telegram did not confirm acceptance.");
        }
        return (int)response.StatusCode;
    }
}
public sealed class NotificationEngine(IDataProtectionProvider protection, IWebhookSender sender, WriteCoordinator writes)
{
    private Task<int> SendAsync(string kind, string encryptedDestination, string payload, CancellationToken ct)
    {
        var destination = protection.CreateProtector("webhook-url").Unprotect(encryptedDestination);
        if (kind == "telegram") {
            var telegram = JsonSerializer.Deserialize<TelegramDestination>(destination) ?? throw new InvalidDataException();
            return sender.SendAsync(TelegramNotifications.Endpoint(telegram), TelegramNotifications.Payload(telegram, payload), ct);
        }
        if (kind != "webhook") throw new InvalidDataException("Unsupported notification channel.");
        return sender.SendAsync(new Uri(destination), payload, ct);
    }
    public async Task QueueAsync(HomelabDb db, NotificationChannel channel, DateTime now, CancellationToken ct)
    {
        if (!channel.Enabled || channel.TestedUtc is null) return;
        var findings = await db.Findings.ToListAsync(ct);
        foreach (var finding in findings) {
            var kind = finding.State == "resolved" ? "resolved" : "opened";
            var prior = await db.Deliveries.Where(d => d.FindingId == finding.Id && d.ChannelRevision == channel.Revision).OrderByDescending(d => d.CreatedUtc).FirstOrDefaultAsync(ct);
            if (kind == "resolved" && prior is null) continue;
            if (finding.SnoozedUntilUtc > now && finding.Severity != "critical") continue;
            var eventKey = $"{channel.Revision:N}:{finding.Id:N}:{kind}:{finding.Severity}:{finding.ReminderSequence}";
            if (await db.Deliveries.AnyAsync(d => d.EventKey == eventKey, ct)) continue;
            if (channel.MaintenanceUntilUtc > now && finding.Severity != "critical") continue;
            var nas = await db.Connections.FindAsync([finding.ConnectionId], ct);
            var disk = finding.DiskId is {} id ? await db.Disks.FindAsync([id], ct) : null;
            var coverage = await db.Capabilities.Where(c => c.ConnectionId == finding.ConnectionId).ToListAsync(ct);
            db.Deliveries.Add(new() { FindingId = finding.Id, ChannelRevision = channel.Revision, EventKey = eventKey, Kind = prior is null ? "opened" : kind == "resolved" ? "resolved" : finding.ReminderSequence > 0 ? "reminder" : "escalated", PayloadJson = JsonSerializer.Serialize(new {
                eventId = eventKey, findingId = finding.Id, severity = finding.Severity, state = finding.State,
                title = channel.MaskIdentifiers ? "NAS observation requires review" : finding.Title,
                nas = channel.MaskIdentifiers ? null : nas?.Name, serial = channel.MaskIdentifiers ? null : disk?.Serial,
                bay = channel.MaskIdentifiers || disk?.BayVerified != true || disk.MappingConfidence == "ambiguous" ? "unverified or masked" : disk.Bay,
                finding.FirstSeenUtc, finding.LastSeenUtc,
                evidence = channel.MaskIdentifiers ? "Open the application for private evidence." : finding.Evidence,
                nextStep = channel.MaskIdentifiers ? "Review evidence and recovery readiness in Homelab Control." : finding.NextStep,
                coverage = coverage.Select(c => new { c.Name, status = c.EffectiveStatus(now), c.LastSuccessUtc }),
                evidencePath = "/?finding=" + finding.Id, limitation = "Observed condition, not a predicted failure date. Channel acceptance does not prove the notification was read."
            }) });
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task DeliverOneAsync(HomelabDb db, NotificationChannel channel, DateTime now, CancellationToken ct)
    {
        NotificationDelivery? item;
        string protectedUrl, channelKind;
        using (var claim = await writes.EnterAsync(ct)) {
            await db.Entry(channel).ReloadAsync(ct);
            if (db.Entry(channel).State == EntityState.Detached || !channel.Enabled || channel.TestedUtc is null) return;
            item = await db.Deliveries.Where(d => d.Status == "pending" && d.ChannelRevision == channel.Revision && d.NextAttemptUtc <= now).OrderBy(d => d.CreatedUtc).FirstOrDefaultAsync(ct);
            if (item is null) return;
            if (item.Attempts >= 5) {
                // A process can stop after persisting its last claim but before recording
                // the receiver response. Exhausted claims must not send a sixth request.
                item.Status = "failed";
                db.DeliveryAttempts.Add(new() { DeliveryId = item.Id, Result = "outcome-unknown" });
                await db.SaveChangesAsync(ct); return;
            }
            var finding = await db.Findings.FindAsync([item.FindingId], ct);
            if (finding is not null) await db.Entry(finding).ReloadAsync(ct);
            if ((channel.MaintenanceUntilUtc > now || finding?.SnoozedUntilUtc > now) && finding?.Severity != "critical") {
                item.NextAttemptUtc = now.AddSeconds(30); await db.SaveChangesAsync(ct); return;
            }
            item.Attempts++;
            // Persist the claim before releasing the writer. A restart retries the same event ID.
            item.NextAttemptUtc = now.AddMinutes(1);
            protectedUrl = channel.ProtectedUrl; channelKind = channel.Kind;
            await db.SaveChangesAsync(ct);
        }
        int? status = null; string result;
        try { status = await SendAsync(channelKind, protectedUrl, item.PayloadJson, ct); result = status is >= 200 and <= 299 ? "accepted" : "rejected"; }
        catch (Exception) when (!ct.IsCancellationRequested) { result = "unavailable"; }
        using var completion = await writes.EnterAsync(ct);
        await db.Entry(item).ReloadAsync(ct);
        if (db.Entry(item).State == EntityState.Detached) return;
        // A settings edit can cancel this claim while the request is in flight. Record what
        // the receiver did, but never revive a cancelled delivery or overwrite new settings.
        if (item.Status == "pending") {
            if (result == "accepted") { item.Status = "accepted"; item.AcceptedUtc = DateTime.UtcNow; }
            else { item.Status = item.Attempts >= 5 ? "failed" : "pending"; item.NextAttemptUtc = now.AddSeconds(Math.Min(900, 30 * Math.Pow(2, item.Attempts))); }
        }
        db.DeliveryAttempts.Add(new() { DeliveryId = item.Id, HttpStatus = status, Result = result }); await db.SaveChangesAsync(ct);
    }
    public async Task<IResult> TestChannelAsync(HomelabDb db, CancellationToken ct)
    {
        NotificationChannel channel;
        Guid revision;
        string protectedUrl, channelKind;
        using (var claim = await writes.EnterAsync(ct)) {
            var current = await db.NotificationChannels.SingleOrDefaultAsync(ct);
            if (current is null) return Results.BadRequest(new { error = "Configure a destination first." });
            channel = current; revision = channel.Revision; protectedUrl = channel.ProtectedUrl; channelKind = channel.Kind;
        }
        int status;
        try {
            status = await SendAsync(channelKind, protectedUrl, "{\"type\":\"homelab-test\",\"message\":\"Homelab Control notification test\"}", ct);
        } catch (Exception) when (!ct.IsCancellationRequested) {
            return Results.BadRequest(new { error = "Test failed. Check hostname enrollment, reachability, and TLS trust." });
        }
        using var completion = await writes.EnterAsync(ct);
        await db.Entry(channel).ReloadAsync(ct);
        db.Activity.Add(new() { Action = "Notification channel test", Detail = "Channel revision " + revision + "; HTTP status " + status });
        if (db.Entry(channel).State == EntityState.Detached || channel.Revision != revision) {
            await db.SaveChangesAsync(ct);
            return Results.Conflict(new { error = "The notification destination changed while its test was in progress. Test the current destination before enabling delivery." });
        }
        if (status is >= 200 and <= 299) channel.TestedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return status is >= 200 and <= 299 ? Results.Ok() : Results.BadRequest(new { error = "Notifier did not accept the test." });
    }

}
public sealed class NotificationWorker(IServiceScopeFactory scopes, WriteCoordinator writes, NotificationEngine engine, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested) {
            try {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
                NotificationChannel? channel;
                using (var lease = await writes.EnterAsync(stop)) {
                    await FindingLifecycle.ExpireAsync(db, DateTime.UtcNow, stop);
                    channel = await db.NotificationChannels.SingleOrDefaultAsync(stop);
                    if (channel is not null) await engine.QueueAsync(db, channel, DateTime.UtcNow, stop);
                }
                if (channel is not null) await engine.DeliverOneAsync(db, channel, DateTime.UtcNow, stop);
            } catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
              catch (Exception ex) { logger.LogWarning("Notification processing failed ({ErrorType}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stop); } catch (OperationCanceledException) { break; }
        }
    }
}
public static class NotificationEndpoints
{
    public static void MapNotifications(this RouteGroupBuilder api)
    {
        api.MapGet("/notifications", async (HomelabDb db) => new { channel = await db.NotificationChannels.Select(c => new { c.Kind, c.Host, c.Enabled, c.MaskIdentifiers, c.TestedUtc, c.MaintenanceUntilUtc }).SingleOrDefaultAsync(), deliveries = await db.Deliveries.OrderByDescending(d => d.CreatedUtc).Take(100).Select(d => new { d.Id, d.FindingId, d.Kind, d.Status, d.Attempts, d.CreatedUtc, d.AcceptedUtc }).ToListAsync(), attempts = await db.DeliveryAttempts.OrderByDescending(a => a.Id).Take(100).ToListAsync() });
        api.MapPut("/notifications", async (ChannelInput input, HomelabDb db, IConfiguration config, IDataProtectionProvider protection) => {
            var channel = await db.NotificationChannels.SingleOrDefaultAsync();
            var kind = input.Kind ?? channel?.Kind ?? "webhook";
            if (kind is not ("webhook" or "telegram")) return Results.BadRequest(new { error = "Choose Telegram or HTTPS webhook." });
            if (kind == "telegram" && (!string.IsNullOrEmpty(input.BotToken) || !string.IsNullOrEmpty(input.ChatId))) {
                var destination = new TelegramDestination(input.BotToken ?? "", input.ChatId ?? "");
                if (!TelegramNotifications.IsValid(destination)) return Results.BadRequest(new { error = "Enter both a valid Telegram bot token and chat ID when setting or changing the destination." });
                channel ??= new NotificationChannel();
                channel.Kind = kind; channel.Host = "api.telegram.org";
                channel.ProtectedUrl = protection.CreateProtector("webhook-url").Protect(JsonSerializer.Serialize(destination));
                channel.TestedUtc = null; channel.Enabled = false; channel.Revision = Guid.NewGuid();
                if (db.Entry(channel).State == EntityState.Detached) db.NotificationChannels.Add(channel);
            }
            if (kind == "webhook" && !string.IsNullOrWhiteSpace(input.Url)) {
                if (!Uri.TryCreate(input.Url, UriKind.Absolute, out var uri) || !WebhookSender.IsAllowedUrl(uri, config)) return Results.BadRequest(new { error = "Use HTTPS and enroll the notifier hostname in Homelab__WebhookHosts__0 on the server." });
                var nasHosts = (await db.Connections.ToListAsync()).SelectMany(n => new[] { new Uri(n.ApiUri).IdnHost, n.ConnectHost });
                if (nasHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "The notifier must not be a monitored NAS endpoint." });
                channel ??= new NotificationChannel();
                channel.Kind = "webhook"; channel.Host = uri.IdnHost; channel.ProtectedUrl = protection.CreateProtector("webhook-url").Protect(input.Url); channel.TestedUtc = null; channel.Enabled = false; channel.Revision = Guid.NewGuid();
                if (db.Entry(channel).State == EntityState.Detached) db.NotificationChannels.Add(channel);
            }
            if (channel is null) return Results.BadRequest(new { error = "Configure a destination first." });
            if (channel.Kind != kind) return Results.BadRequest(new { error = "Provide the destination details when changing channel type." });
            if (input.Enabled && channel.TestedUtc is null) return Results.BadRequest(new { error = "Send a successful test before enabling delivery." });
            if (input.MaintenanceUntilUtc > DateTime.UtcNow.AddDays(7)) return Results.BadRequest(new { error = "Maintenance suppression is limited to seven days." });
            if (channel.MaskIdentifiers != input.MaskIdentifiers) channel.Revision = Guid.NewGuid();
            foreach (var pending in await db.Deliveries.Where(d => d.Status == "pending" && d.ChannelRevision != channel.Revision).ToListAsync()) pending.Status = "cancelled-channel-changed";
            channel.Enabled = input.Enabled; channel.MaskIdentifiers = input.MaskIdentifiers; channel.MaintenanceUntilUtc = input.MaintenanceUntilUtc;
            await db.SaveChangesAsync(); return Results.Ok();
        });
        api.MapPost("/notifications/test", (HomelabDb db, NotificationEngine engine, CancellationToken ct) => engine.TestChannelAsync(db, ct));
    }
}
public record ChannelInput(string? Url, bool Enabled, bool MaskIdentifiers, DateTime? MaintenanceUntilUtc, string? Kind = null, string? BotToken = null, string? ChatId = null);
