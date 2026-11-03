using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.SmartReports;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.ImportReport;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/smart-reports");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Import Report");
        Description(b =>
        {
            b.WithName("StorageSmartImportReport");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>("application/json");
            b.Produces<Response>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var input = request.Input;
        var context = HttpContext;
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        SmartReportPreview preview;
        try
        {
            preview = Parse(disk, input, DateTime.UtcNow);
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

        if (input.ExpectedHash != preview.ReportHash || input.ConfirmedSerial != disk.Serial.Trim())
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Preview this exact report and retype the selected drive serial before importing." }));
            return;
        }

        if (await db.Disks.AnyAsync(x => x.ConnectionId == disk.ConnectionId && x.Id != disk.Id && x.Serial == disk.Serial && x.Model == disk.Model))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Another drive has the same model and serial. Resolve identity ambiguity first." }));
            return;
        }

        if (await db.SmartReports.AnyAsync(x => x.DiskId == id && (x.ReportHash == preview.ReportHash || x.ReportHash == preview.LegacyReportHash || x.ReportHash == preview.PreviousReportHash || x.ReportHash == preview.BeforeOverallReportHash || x.ReportHash == preview.ScsiV1ReportHash || x.ReportHash == preview.BeforeDeviceInfoHash || x.ReportHash == preview.BeforeSelfTestsHash)))
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This report was already imported. Existing evidence is immutable." }));
            return;
        }

        // Persist the overall assessment as its own binary observation, not a
        // replacement for any detailed metric. One means reported failure.
        var overall = preview.OverallSmart;
        var importedReadings = preview.Readings.Append(new ReportReading("smart.overall_failed", overall?.Passed is { } passed ? passed ? 0 : 1 : null, "flag", overall?.Quality ?? "unknown", overall?.Passed is null ? "Overall SMART result was not reported as a boolean." : null));
        var observations = importedReadings.Select(x => new Observation { SourceKey = $"smartctl:{id:N}:{preview.ReportHash}:{x.Metric}", Metric = x.Metric, Value = x.Value, Unit = x.Unit, Quality = x.Quality, MissingReason = x.MissingReason, ObservedUtc = preview.ObservedUtc, Source = SmartReports.Source, SourceSchema = preview.SourceSchema, ResetEpoch = preview.ContinuityGroup }).ToArray();
        foreach (var observation in observations)
        {
            if (ObservationValidation.Validate(observation, DateTime.UtcNow)is { } error)
            {
                await Send.ResultAsync(Results.BadRequest(new { error }));
                return;
            }

            if (await db.Observations.AnyAsync(x => x.ConnectionId == disk.ConnectionId && x.SourceKey == observation.SourceKey))
            {
                await Send.ResultAsync(Results.Conflict(new { error = "Report evidence identifiers already exist; no readings were imported." }));
                return;
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var report = new SmartReport
        {
            DiskId = id,
            ReportHash = preview.ReportHash,
            Actor = context.User.Identity!.Name!,
            CuratedJson = JsonSerializer.Serialize(preview)
        };
        db.SmartReports.Add(report);
        await SelfTestFindings.StageAsync(db, disk, report, preview);
        foreach (var observation in observations)
            await ObservationIngestion.StageAsync(db, disk, observation, report.Actor);
        db.Activity.Add(new() { Actor = report.Actor, Action = "SMART report imported", Detail = $"Disk {id}; report {report.Id}; {observations.Length} curated readings." });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await Send.ResultAsync(Results.Ok(new Response(report.Id, observations.Length)));
        return;
    }
}
