using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.SmartReports;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.PreviewReport;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.SmartReportPreview>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/smart-reports/preview");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Preview Report");
        Description(b =>
        {
            b.WithName("StorageSmartPreviewReport");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.SmartReportPreview>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var input = request.Input;
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        try
        {
            await Send.ResultAsync(Results.Ok(Parse(disk, input, DateTime.UtcNow)));
            return;
        }
        catch (JsonException)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "The report is not supported JSON or exceeds nesting limits." }));
            return;
        }
        catch (InvalidDataException ex)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = ex.Message }));
            return;
        }
    }
}
