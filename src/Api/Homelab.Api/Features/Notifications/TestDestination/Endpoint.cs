using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Notifications.TestDestination;
public sealed class Endpoint(HomelabDb db, NotificationEngine engine) : Endpoint<EmptyRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/notifications/test");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Test Destination");
        Options(b => b.WithMetadata(new OwnsWriteLease()));
        Description(b =>
        {
            b.WithName("NotificationsTestDestination");
            b.WithTags("Notifications");
            b.Produces(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(await engine.TestChannelAsync(db, ct));
        return;
    }
}
