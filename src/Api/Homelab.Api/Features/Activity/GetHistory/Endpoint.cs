using Microsoft.EntityFrameworkCore;
using static Homelab.Api.ActivityHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Activity.GetHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.ActivityHistoryPage>
{
    public override void Configure()
    {
        Get("/api/v1/activity/history");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("ActivityGetHistory");
            b.WithTags("Activity");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Homelab.Api.ActivityHistoryPage>(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var snapshotId = request.SnapshotId;
        var before = request.Before;
        if (snapshotId < 0 || before <= 0 || before is not null && (snapshotId is null || before > snapshotId))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Use a valid activity snapshot and cursor." }));
            return;
        }

        await Send.ResultAsync(Results.Ok(await ReadAsync(db, snapshotId, before, ct)));
        return;
    }
}
