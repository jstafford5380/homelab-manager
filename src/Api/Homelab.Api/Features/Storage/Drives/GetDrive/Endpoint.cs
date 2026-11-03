using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetDrive;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Drive");
        Description(b =>
        {
            b.WithName("StorageDrivesGetDrive");
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
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        await Send.ResultAsync(Results.Ok(new Response(disk, await db.DiskAliases.Where(x => x.DiskId == id).OrderByDescending(x => x.FirstSeenUtc).ToListAsync(), await db.DiskAnnotations.Where(x => x.DiskId == id).OrderByDescending(x => x.Id).Take(100).ToListAsync(), await db.Findings.Where(x => x.DiskId == id).ToListAsync(), await db.Plans.Where(x => x.DiskId == id).ToListAsync())));
        return;
    }
}
