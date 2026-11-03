using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Pools.GetHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/pools/{guid}/history");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("StoragePoolsGetHistory");
            b.WithTags("Connections");
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
        var guid = request.Guid;
        var before = request.Before;
        if (!await db.Connections.AnyAsync(x => x.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var rows = await db.PoolSnapshots.AsNoTracking().Where(x => x.ConnectionId == id && x.PoolGuid == guid && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(101).ToListAsync();
        await Send.ResultAsync(Results.Ok(new Response(rows.Take(100).Select((row, index) =>
        {
            var result = JsonSerializer.SerializeToNode(row, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            result.AsObject().Remove("sourceContextJson"); // Internal identity/timing guard, not a new UI identifier.
            result["vdevComparison"] = JsonSerializer.SerializeToNode(VdevChanges.Compare(row, index + 1 < rows.Count ? rows[index + 1] : null), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result;
        }), rows.Count > 100 ? (long? )rows[99].Id : null)));
        return;
    }
}
