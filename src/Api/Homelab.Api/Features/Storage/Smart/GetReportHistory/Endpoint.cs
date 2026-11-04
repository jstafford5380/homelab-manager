using Microsoft.EntityFrameworkCore;
using static Homelab.Api.SmartReports;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.GetReportHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.SmartReports.HistoryPage>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/smart-reports/history");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get Report History");
        Description(b =>
        {
            b.WithName("StorageSmartGetReportHistory");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Homelab.Api.SmartReports.HistoryPage>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var asOf = request.AsOf;
        var offset = request.Offset;
        var now = DateTime.UtcNow;
        if (offset < 0 || offset > int.MaxValue - 50 || offset > 0 && asOf is null || asOf is { Kind: not DateTimeKind.Utc } || asOf > now)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Use a valid UTC history snapshot and nonnegative offset." }));
            return;
        }

        if (!await db.Disks.AnyAsync(x => x.Id == id, ct))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        await Send.ResultAsync(Results.Ok(await ReadHistoryAsync(db, id, asOf ?? now, offset ?? 0, ct)));
        return;
    }
}
