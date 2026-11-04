using Microsoft.EntityFrameworkCore;
using static Homelab.Api.StorageSmart;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.GetTrends;
public sealed class Endpoint(HomelabDb db, IConfiguration configuration) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/storage/drives/{id:guid}/trends");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Trends");
        Description(b =>
        {
            b.WithName("StorageSmartGetTrends");
            b.WithTags("Storage");
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
        if (!configuration.GetValue<bool>("Homelab:SmartEnabled"))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (!await db.Disks.AnyAsync(x => x.Id == id, ct))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-56);
        var now = DateTime.UtcNow;
        var metrics = new[]
        {
            "ata.pending_sectors",
            "ata.reallocated_sectors",
            "ata.offline_uncorrectable",
            "ata.crc_errors",
            "nvme.media_errors",
            "scsi.grown_defects"
        };
        var rows = await db.Observations.AsNoTracking().Where(x => x.DiskId == id && metrics.Contains(x.Metric) && x.ObservedUtc >= cutoff && x.ObservedUtc <= now).OrderByDescending(x => x.ObservedUtc).ThenByDescending(x => x.Id).Take(10001).ToListAsync(ct);
        if (rows.Count > 10000)
        {
            await Send.ResultAsync(Results.Ok(new Response(Array.Empty<StorageTrend>(), "Too many readings to show a complete 8-week trend.")));
            return;
        }

        await Send.ResultAsync(Results.Ok(new Response(metrics.Select(m => Trend(m, rows)).OfType<StorageTrend>().ToArray(), "")));
        return;
    }
}
