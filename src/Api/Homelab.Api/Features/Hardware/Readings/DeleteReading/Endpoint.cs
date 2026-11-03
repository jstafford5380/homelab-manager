using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Readings.DeleteReading;
public sealed class Endpoint(HomelabDb db, IConfiguration config) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Delete("/api/v1/hardware/servers/{id:guid}/readings/{readingId:guid}");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Delete Reading");
        Description(b =>
        {
            b.WithName("HardwareReadingsDeleteReading");
            b.WithTags("Hardware");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces(204);
            b.Produces(404);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var readingId = request.ReadingId;
        var revision = request.Revision;
        if (await Server(id, db, config)is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var row = await db.HardwareReadings.SingleOrDefaultAsync(r => r.ServerId == id && r.Id == readingId && r.Source == "entered" && r.DeletedUtc == null);
        if (row is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (row.Revision != revision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This reading changed. Reload before deleting." }));
            return;
        }

        row.DeletedUtc = DateTime.UtcNow;
        row.Revision = Guid.NewGuid();
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.NoContent());
        return;
    }
}
