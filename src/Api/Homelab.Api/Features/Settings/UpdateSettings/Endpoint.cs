using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Settings.UpdateSettings;
public sealed class Endpoint(HomelabDb db, BackupService backups) : Endpoint<Request, Homelab.Api.OperatingSettings>
{
    public override void Configure()
    {
        Put("/api/v1/settings");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Settings");
        Description(b =>
        {
            b.WithName("SettingsUpdateSettings");
            b.WithTags("Settings");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.OperatingSettings>(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(input.Timezone);
        }
        catch
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose a valid timezone." }));
            return;
        }

        if (input.DriveAgeReviewYears is < 1 or > 30 || input.WarrantyNoticeDays is < 0 or > 730 || input.CapacityUnits is not ("binary" or "decimal") || input.InventoryMinutes is < 1 or > 1440 || input.SmartMinutes is < 15 or > 10080 || input.SampleRetentionDays is < 7 or > 3650 || input.RawRetentionDays is < 0 or > 90 || input.BackupDailyCopies is < 1 or > 90 || input.BackupWeeklyCopies is < 1 or > 52)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "One or more settings are outside supported limits." }));
            return;
        }

        if (!double.IsFinite(input.HardwareCpuPercent) || input.HardwareCpuPercent is < 1 or > 100 || !double.IsFinite(input.HardwareMemoryPercent) || input.HardwareMemoryPercent is < 1 or > 100 || !double.IsFinite(input.HardwareTemperatureC) || input.HardwareTemperatureC is < -50 or > 200 || input.HardwareSustainedMinutes is < 5 or > 1440)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Hardware review levels are outside supported limits." }));
            return;
        }

        if (TemperaturePolicy.Validate(input)is { } policyError)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = policyError }));
            return;
        }

        if (input.BackupsEnabled && backups.Destination is null)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Configure the server backup destination first." }));
            return;
        }

        input.Id = 1;
        var stored = await db.Settings.FindAsync(1);
        if (stored is null)
            db.Settings.Add(input);
        else
            db.Entry(stored).CurrentValues.SetValues(input);
        db.Activity.Add(new() { Action = "Operating settings changed" });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(input));
        return;
    }
}
