using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetAcquisition;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/acquisition");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Acquisition");
        Description(b =>
        {
            b.WithName("StorageDrivesGetAcquisition");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        if (!await db.Disks.AnyAsync(x => x.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        await Send.ResultAsync(Results.Ok(new Response(await db.DriveAcquisitions.AsNoTracking().SingleOrDefaultAsync(x => x.DiskId == id))));
        return;
    }
}
