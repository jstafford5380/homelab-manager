using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Notifications.GetSettings;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/notifications");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Settings");
        Description(b =>
        {
            b.WithName("NotificationsGetSettings");
            b.WithTags("Notifications");
            b.Produces<Response>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(Results.Ok(new Response(await db.NotificationChannels.Select(c => new ResponseChannel(c.Kind, c.Host, c.Enabled, c.MaskIdentifiers, c.TestedUtc, c.MaintenanceUntilUtc)).SingleOrDefaultAsync(), await db.Deliveries.OrderByDescending(d => d.CreatedUtc).Take(100).Select(d => new ResponseDeliveriesItem(d.Id, d.FindingId, d.Kind, d.Status, d.Attempts, d.CreatedUtc, d.AcceptedUtc)).ToListAsync(), await db.DeliveryAttempts.OrderByDescending(a => a.Id).Take(100).ToListAsync())));
        return;
    }
}
