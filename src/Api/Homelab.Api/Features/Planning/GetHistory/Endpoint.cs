using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Planning.GetHistory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/plans/{id:guid}/history");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get History");
        Description(b =>
        {
            b.WithName("PlanningGetHistory");
            b.WithTags("Planning");
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
        var before = request.Before;
        if (!await db.Plans.AnyAsync(x => x.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var rows = await db.PlanChanges.AsNoTracking().Where(x => x.PlanId == id && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(51).ToListAsync();
        await Send.ResultAsync(Results.Ok(new Response(rows.Take(50).Select(x => new ResponseItemsItem(x.Id, x.AtUtc, x.Actor, x.BeforeJson == null ? null : JsonSerializer.Deserialize<PlanningItem>(x.BeforeJson), JsonSerializer.Deserialize<PlanningItem>(x.AfterJson))), rows.Count > 50 ? (long? )rows[49].Id : null)));
        return;
    }
}
