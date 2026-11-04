using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, System.Collections.Generic.IEnumerable<Homelab.Api.StorageHistoryItem>>
{
    public override void Configure()
    {
        Get("/api/v1/storage/drives/{id:guid}/history");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("StorageDrivesGetHistory");
            b.WithTags("Storage");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<System.Collections.Generic.IEnumerable<Homelab.Api.StorageHistoryItem>>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var disk = await db.Disks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var history = new List<StorageHistoryItem>
        {
            new(disk.FirstSeenUtc, "discovered", "First discovered on this server.")
        };
        var acquisition = await db.DriveAcquisitions.AsNoTracking().SingleOrDefaultAsync(x => x.DiskId == id, ct);
        if (acquisition?.InstalledUtc is { } installed)
            history.Add(new(installed, "installed", "Installed" + (acquisition.InstallationConfidence == "estimated" ? " (estimated)." : ".")));
        if (disk.PurchasedUtc is { } purchased)
            history.Add(new(purchased, "purchased", "Purchased" + (disk.PurchaseDateConfidence == "estimated" ? " (estimated)." : ".")));
        history.AddRange((await db.Maintenance.AsNoTracking().Where(x => x.DiskId == id && x.VoidedUtc == null).OrderByDescending(x => x.OccurredUtc).Take(100).ToListAsync(ct)).Select(x => new StorageHistoryItem(x.OccurredUtc, "note", x.Summary)));
        foreach (var f in await db.Findings.AsNoTracking().Where(x => x.DiskId == id).OrderByDescending(x => x.FirstSeenUtc).Take(100).ToListAsync(ct))
        {
            history.Add(new(f.FirstSeenUtc, "finding", f.Title));
            if (f.ResolvedUtc is { } resolved)
                history.Add(new(resolved, "resolved", "Resolved: " + f.Title));
        }

        history.AddRange((await db.Replacements.AsNoTracking().Where(x => (x.OldDiskId == id || x.NewDiskId == id) && x.VoidedUtc == null).OrderByDescending(x => x.OccurredUtc).Take(100).ToListAsync(ct)).Select(x => new StorageHistoryItem(x.OccurredUtc, "replacement", x.OldDiskId == id ? "Replaced by " + x.NewIdentity : "Replaced " + x.OldIdentity)));
        await Send.ResultAsync(Results.Ok(history.OrderByDescending(x => x.AtUtc).Take(100)));
        return;
    }
}
