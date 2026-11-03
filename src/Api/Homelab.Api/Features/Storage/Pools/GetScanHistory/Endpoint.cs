using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Pools.GetScanHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/pools/{guid}/scans");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Scan History");
        Description(b =>
        {
            b.WithName("StoragePoolsGetScanHistory");
            b.WithTags("Connections");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var guid = request.Guid;
        var before = request.Before;
        if (!await db.Connections.AnyAsync(x => x.Id == id, ct))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (before is <= 0)
        {
            await Send.ResultAsync(Results.BadRequest());
            return;
        }

        var rows = await db.PoolScans.AsNoTracking().Where(x => x.ConnectionId == id && x.PoolGuid == guid && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(51).ToListAsync(ct);
        await Send.ResultAsync(Results.Ok(new Response(rows.Take(50), rows.Count > 50 ? (long? )rows[49].Id : null)));
        return;
    }
}
