using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.ListReports;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, System.Collections.Generic.IEnumerable<ResponseItem>>
{
    public override void Configure()
    {
        Get("/api/v1/disks/{id:guid}/smart-reports");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "List Reports");
        Description(b =>
        {
            b.WithName("StorageSmartListReports");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<System.Collections.Generic.IEnumerable<ResponseItem>>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        if (!await db.Disks.AnyAsync(x => x.Id == id))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var rows = await db.SmartReports.AsNoTracking().Where(x => x.DiskId == id).OrderByDescending(x => x.ImportedUtc).Take(50).ToListAsync();
        await Send.ResultAsync(Results.Ok(rows.Select(x => new ResponseItem(x.Id, x.Actor, x.ImportedUtc, JsonSerializer.Deserialize<SmartReportPreview>(x.CuratedJson)))));
        return;
    }
}
