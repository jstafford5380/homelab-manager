using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Settings.GetSettings;
public sealed class Endpoint(HomelabDb db, BackupService backups) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/settings");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Settings");
        Description(b =>
        {
            b.WithName("GetSettings");
            b.WithTags("Settings");
            b.Produces<Response>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(Results.Ok(new Response(await db.Settings.SingleOrDefaultAsync() ?? new OperatingSettings(), backups.Destination is not null)));
        return;
    }
}
