using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Notifications.UpdateSettings;
public sealed class Endpoint(HomelabDb db, IConfiguration config, IDataProtectionProvider protection) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Put("/api/v1/notifications");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Settings");
        Description(b =>
        {
            b.WithName("NotificationsUpdateSettings");
            b.WithTags("Notifications");
            b.Accepts<Request>("application/json");
            b.Produces(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        var channel = await db.NotificationChannels.SingleOrDefaultAsync();
        var kind = input.Kind ?? channel?.Kind ?? "webhook";
        if (kind is not ("webhook" or "telegram"))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose Telegram or HTTPS webhook." }));
            return;
        }

        if (kind == "telegram" && (!string.IsNullOrEmpty(input.BotToken) || !string.IsNullOrEmpty(input.ChatId)))
        {
            var destination = new TelegramDestination(input.BotToken ?? "", input.ChatId ?? "");
            if (!TelegramNotifications.IsValid(destination))
            {
                await Send.ResultAsync(Results.BadRequest(new { error = "Enter both a valid Telegram bot token and chat ID when setting or changing the destination." }));
                return;
            }

            channel ??= new NotificationChannel();
            channel.Kind = kind;
            channel.Host = "api.telegram.org";
            channel.ProtectedUrl = protection.CreateProtector("webhook-url").Protect(JsonSerializer.Serialize(destination));
            channel.TestedUtc = null;
            channel.Enabled = false;
            channel.Revision = Guid.NewGuid();
            if (db.Entry(channel).State == EntityState.Detached)
                db.NotificationChannels.Add(channel);
        }

        if (kind == "webhook" && !string.IsNullOrWhiteSpace(input.Url))
        {
            if (!Uri.TryCreate(input.Url, UriKind.Absolute, out var uri) || !WebhookSender.IsAllowedUrl(uri, config))
            {
                await Send.ResultAsync(Results.BadRequest(new { error = "Use HTTPS and enroll the notifier hostname in Homelab__WebhookHosts__0 on the server." }));
                return;
            }

            var nasHosts = (await db.Connections.ToListAsync()).SelectMany(n => new[] { new Uri(n.ApiUri).IdnHost, n.ConnectHost });
            if (nasHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
            {
                await Send.ResultAsync(Results.BadRequest(new { error = "The notifier must not be a monitored NAS endpoint." }));
                return;
            }

            channel ??= new NotificationChannel();
            channel.Kind = "webhook";
            channel.Host = uri.IdnHost;
            channel.ProtectedUrl = protection.CreateProtector("webhook-url").Protect(input.Url);
            channel.TestedUtc = null;
            channel.Enabled = false;
            channel.Revision = Guid.NewGuid();
            if (db.Entry(channel).State == EntityState.Detached)
                db.NotificationChannels.Add(channel);
        }

        if (channel is null)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Configure a destination first." }));
            return;
        }

        if (channel.Kind != kind)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Provide the destination details when changing channel type." }));
            return;
        }

        if (input.Enabled && channel.TestedUtc is null)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Send a successful test before enabling delivery." }));
            return;
        }

        if (input.MaintenanceUntilUtc > DateTime.UtcNow.AddDays(7))
        {
            const string error = "Maintenance suppression is limited to seven days.";
            await Send.ResultAsync(Results.BadRequest(new
            {
                error,
                errors = new Dictionary<string, string[]> { ["maintenanceUntilUtc"] = [error] }
            }));
            return;
        }

        if (channel.MaskIdentifiers != input.MaskIdentifiers)
            channel.Revision = Guid.NewGuid();
        foreach (var pending in await db.Deliveries.Where(d => d.Status == "pending" && d.ChannelRevision != channel.Revision).ToListAsync())
            pending.Status = "cancelled-channel-changed";
        channel.Enabled = input.Enabled;
        channel.MaskIdentifiers = input.MaskIdentifiers;
        channel.MaintenanceUntilUtc = input.MaintenanceUntilUtc;
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
