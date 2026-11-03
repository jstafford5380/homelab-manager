using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DrivePaths;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetHardwarePaths;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/hardware-paths");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Hardware Paths");
        Description(b =>
        {
            b.WithName("StorageDrivesGetHardwarePaths");
            b.WithTags("Storage.Drives");
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
        var before = request.Before;
        var disk = await db.Disks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (before is <= 0)
        {
            await Send.ResultAsync(Results.BadRequest());
            return;
        }

        var now = DateTime.UtcNow;
        var latestIds = db.DrivePaths.GroupBy(x => new { x.DiskId, x.AssetId }).Select(g => g.Max(x => x.Id));
        var ownAssetIds = db.DrivePaths.Where(x => x.DiskId == id).Select(x => x.AssetId).Distinct();
        var latest = await db.DrivePaths.AsNoTracking().Where(x => latestIds.Contains(x.Id) && ownAssetIds.Contains(x.AssetId)).ToListAsync();
        var assets = await db.Assets.AsNoTracking().Where(x => ownAssetIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var relatedDiskIds = latest.Select(x => x.DiskId).Append(id).Distinct().ToArray();
        var disks = await db.Disks.AsNoTracking().Where(x => relatedDiskIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var current = latest.Where(x => disks.TryGetValue(x.DiskId, out var d) && assets.TryGetValue(x.AssetId, out var a) && Status(x, d, a, true, now) == "recorded relationship").ToArray();
        var sharedIds = current.Where(x => x.DiskId == id).Select(x => x.AssetId).ToHashSet();
        var peers = current.Where(x => x.DiskId != id && sharedIds.Contains(x.AssetId)).ToArray();
        var peerIds = peers.Select(x => x.DiskId).Distinct().Order().Take(33).ToArray();
        var shown = peerIds.Take(32).ToArray();
        var findings = await db.Findings.AsNoTracking().Where(x => x.DiskId != null && shown.Contains(x.DiskId.Value) && x.State != "resolved").OrderByDescending(x => x.LastSeenUtc).Take(161).ToListAsync();
        var rows = await db.DrivePaths.AsNoTracking().Where(x => x.DiskId == id && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(26).ToListAsync();
        await Send.ResultAsync(Results.Ok(new Response(now, latest.Where(x => x.DiskId == id).Select(x => new ResponseCurrentHeadsItem(x.AssetId, x.Id, x.Attached, x.Evidence, x.RecordedUtc)), rows.Take(25).Select(x => new ResponseItemsItem(x, Status(x, disk, assets[x.AssetId], latest.Any(l => l.Id == x.Id), now))), rows.Count > 25 ? (long? )rows[24].Id : null, peerIds.Length > 32 || findings.Count > 160, shown.Select(peer => new ResponsePeersItem(peer, disks[peer].Serial, disks[peer].Model, peers.Where(x => x.DiskId == peer).Select(x => new ResponsePeersItemComponentsItem(x.AssetId, x.AssetName, x.AssetType, x.ValidUntilUtc)), findings.Take(160).Where(x => x.DiskId == peer).Select(x => new ResponsePeersItemFindingsItem(x.Id, x.Title, x.Evidence, x.EvidenceObservedUtc, x.LastSeenUtc, x.State)))))));
        return;
    }
}
