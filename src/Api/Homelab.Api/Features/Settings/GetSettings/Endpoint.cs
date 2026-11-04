using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Settings.GetSettings;
public sealed class Endpoint(HomelabDb db, BackupService backups) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/settings");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Get Settings");
        Description(b =>
        {
            b.WithName("GetSettings");
            b.WithTags("Settings");
            b.Produces<Response>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var settings = await db.Settings.SingleOrDefaultAsync(ct) ?? new OperatingSettings();
        var states = await db.ApiPowerOnStates.AsNoTracking().ToListAsync(ct);
        var run = states.Select(LastRun).OfType<PowerOnHoursRun>().OrderByDescending(item => item.AtUtc).FirstOrDefault();
        await Send.ResultAsync(Results.Ok(new Response(settings, backups.Destination is not null,
            backups.Destination, SettingsSections.Revisions(settings), run)));
    }

    private static PowerOnHoursRun? LastRun(ApiPowerOnState state)
    {
        var snapshot = ApiPowerOnHours.Read(state.SnapshotJson);
        if (state.LastFailedAttemptUtc is { } failed && (snapshot is null || failed > snapshot.FetchedUtc))
            return new(DateTime.SpecifyKind(failed, DateTimeKind.Utc), "failed", null, null);
        if (snapshot is null)
            return null;

        var available = snapshot.Readings.Count(reading => reading.Status == "supported");
        var total = snapshot.Readings.Length;
        return new(snapshot.FetchedUtc, available == 0 ? "unavailable" : available == total ? "complete" : "partial", available, total);
    }
}
