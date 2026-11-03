using Microsoft.EntityFrameworkCore;
using static Homelab.Api.RecoveryHistory;
using FastEndpoints;

namespace Homelab.Api.Features.Recovery.GetHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.RecoveryHistoryPage>
{
    public override void Configure()
    {
        Get("/api/v1/recovery/{id:guid}/history");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("RecoveryGetHistory");
            b.WithTags("Recovery");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Homelab.Api.RecoveryHistoryPage>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var kind = request.Kind;
        var asOfUtc = request.AsOfUtc;
        var beforeAtUtc = request.BeforeAtUtc;
        var beforeId = request.BeforeId;
        var now = DateTime.UtcNow;
        if (kind is not ("runs" or "exercises") || beforeAtUtc.HasValue != beforeId.HasValue || beforeId == Guid.Empty || beforeAtUtc is { } time && time.Kind != DateTimeKind.Utc || beforeId is not null && asOfUtc is null || asOfUtc is { } anchor && (anchor.Kind != DateTimeKind.Utc || anchor < DateTime.UnixEpoch || anchor > now))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose backup runs or restore exercises with a valid UTC history cursor." }));
            return;
        }

        if (!await db.Recovery.AnyAsync(x => x.Id == id, ct))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        await Send.ResultAsync(Results.Ok(await ReadAsync(db, id, kind, asOfUtc ?? now, beforeId is { } cursor ? new(beforeAtUtc!.Value, cursor) : null, ct)));
        return;
    }
}
