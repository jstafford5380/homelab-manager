using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Pools.GetDailyHistory;
public sealed class Endpoint(HomelabDb db, BackupService backups) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/pools/{guid}/daily-history");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Daily History");
        Description(b =>
        {
            b.WithName("StoragePoolsGetDailyHistory");
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
        var beforeDay = request.BeforeDay;
        var beforeId = request.BeforeId;
        if (!await db.Connections.AnyAsync(x => x.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if ((beforeDay is null) != (beforeId is null))
        {
            await Send.ResultAsync(Results.BadRequest());
            return;
        }

        var rows = await db.PoolDailySummaries.AsNoTracking().Where(x => x.ConnectionId == id && x.PoolGuid == guid && (beforeDay == null || x.DayUtc < beforeDay || x.DayUtc == beforeDay && x.Id < beforeId)).OrderByDescending(x => x.DayUtc).ThenByDescending(x => x.Id).Take(51).ToListAsync();
        var days = (await db.Settings.AsNoTracking().SingleOrDefaultAsync() ?? new OperatingSettings()).SampleRetentionDays;
        var cutoff = PoolRetention.DetailCutoff(DateTime.UtcNow, days);
        var pending = await db.PoolSnapshots.CountAsync(x => x.ConnectionId == id && x.PoolGuid == guid && x.FetchedUtc < cutoff);
        await Send.ResultAsync(Results.Ok(new Response(rows.Take(50), rows.Count > 50 ? (DateTime? )rows[49].DayUtc : null, rows.Count > 50 ? (long? )rows[49].Id : null, days, 2, backups.Destination is not null, pending)));
        return;
    }
}
