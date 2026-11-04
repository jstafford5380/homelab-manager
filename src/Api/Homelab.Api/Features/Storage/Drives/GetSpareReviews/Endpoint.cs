using Microsoft.EntityFrameworkCore;
using static Homelab.Api.SpareCompatibility;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetSpareReviews;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/spare-reviews");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Spare Reviews");
        Description(b =>
        {
            b.WithName("StorageDrivesGetSpareReviews");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var before = request.Before;
        var disk = await db.Disks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (before is <= 0)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Use a positive review cursor." }));
            return;
        }

        var rows = await db.SpareReviews.AsNoTracking().Where(x => x.DiskId == id && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(26).ToListAsync();
        var assetIds = rows.Select(x => x.AssetId).Distinct().ToArray();
        var assets = await db.Assets.AsNoTracking().Where(x => assetIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var latest = await db.SpareReviews.Where(x => x.DiskId == id && assetIds.Contains(x.AssetId)).GroupBy(x => x.AssetId).Select(g => new { AssetId = g.Key, Id = g.Max(x => x.Id) }).ToDictionaryAsync(x => x.AssetId, x => x.Id);
        var now = DateTime.UtcNow;
        await Send.ResultAsync(Results.Ok(new Response(now, rows.Take(25).Select(r => new ResponseItemsItem(r, latest[r.AssetId] != r.Id ? "superseded" : Status(r, disk, assets.GetValueOrDefault(r.AssetId), now))), rows.Count > 25 ? (long? )rows[24].Id : null)));
        return;
    }
}
