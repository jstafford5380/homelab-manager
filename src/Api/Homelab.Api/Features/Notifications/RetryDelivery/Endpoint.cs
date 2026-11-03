using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Notifications.RetryDelivery;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/notifications/deliveries/{id:guid}/retry");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Retry Delivery");
        Description(b =>
        {
            b.WithName("NotificationsRetryDelivery");
            b.WithTags("Notifications");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var delivery = await db.Deliveries.FindAsync(id);
        if (delivery is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var channel = await db.NotificationChannels.SingleOrDefaultAsync();
        if (delivery.Status != "failed" || channel?.Enabled != true || channel.TestedUtc is null || delivery.ChannelRevision != channel.Revision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "Only failed deliveries for the current tested, enabled channel can be retried." }));
            return;
        }

        delivery.Status = "pending";
        delivery.Attempts = 0;
        delivery.NextAttemptUtc = DateTime.UtcNow;
        db.Activity.Add(new() { Action = "Notification retry requested", Detail = id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
