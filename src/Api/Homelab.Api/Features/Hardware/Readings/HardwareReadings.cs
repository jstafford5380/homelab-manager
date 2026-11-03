using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;

namespace Homelab.Api;
public static class HardwareReadings
{
    public static string? Validate(HardwareReadingInput input, double? total, DateTime now)
    {
        if (input.TakenUtc.Kind != DateTimeKind.Utc || input.TakenUtc < DateTime.UnixEpoch || input.TakenUtc > now.AddMinutes(1))
            return "Choose a past reading time in UTC.";
        if (input.Kind is not ("moment" or "average") || input.Kind == "average" && input.PeriodMinutes is not (>= 1 and <= 525600) || input.Kind == "moment" && input.PeriodMinutes is not null)
            return "Specify the duration only for an average reading.";
        if (new[]
        {
            input.CpuPercent,
            input.MemoryUsedBytes,
            input.TemperatureC,
            input.PowerWatts
        }.All(v => v is null))
            return "Enter at least one measurement.";
        if (new[]
        {
            input.CpuPercent,
            input.MemoryUsedBytes,
            input.TemperatureC,
            input.PowerWatts
        }.Any(v => v is { } n && !double.IsFinite(n)) || input.CpuPercent is < 0 or > 100 || input.MemoryUsedBytes is < 0 or > 1e16 || input.MemoryUsedBytes > total || input.TemperatureC is < -50 or > 200 || input.PowerWatts is < 0 or > 1000000 || input.Note is null || input.Note.Length > 2000)
            return "A measurement or note is outside supported limits.";
        return null;
    }

    internal static async Task<IResult> SaveReading(Guid id, Guid? readingId, HardwareReadingInput input, HomelabDb db, IConfiguration config)
    {
        var s = await Server(id, db, config);
        if (s is null)
            return Results.NotFound();
        var memory = await db.HardwareComponents.AsNoTracking().FirstOrDefaultAsync(c => c.ServerId == id && c.Kind == "memory" && c.RetiredUtc == null);
        var total = memory?.DetectedTotalBytes ?? (memory is null ? null : HardwareMetrics.EnteredTotal(memory));
        if (Validate(input, total, DateTime.UtcNow)is { } error)
            return Results.BadRequest(new { error });
        var row = readingId is { } rid ? await db.HardwareReadings.SingleOrDefaultAsync(r => r.Id == rid && r.ServerId == id && r.Source == "entered" && r.DeletedUtc == null) : new HardwareReading
        {
            ServerId = id
        };
        if (row is null)
            return Results.NotFound();
        if (readingId is not null && row.Revision != input.Revision)
            return Results.Conflict(new { error = "This reading changed. Reload before saving." });
        row.TakenUtc = input.TakenUtc;
        row.Kind = input.Kind;
        row.PeriodMinutes = input.PeriodMinutes;
        row.CpuPercent = input.CpuPercent;
        row.MemoryUsedBytes = input.MemoryUsedBytes;
        row.TotalBytes = total;
        row.TemperatureC = input.TemperatureC;
        row.PowerWatts = input.PowerWatts;
        row.Note = input.Note;
        row.Revision = Guid.NewGuid();
        if (readingId is null)
            db.HardwareReadings.Add(row);
        db.Activity.Add(new() { Action = readingId is null ? "Hardware reading added" : "Hardware reading edited", Detail = id + " / " + row.Id });
        await db.SaveChangesAsync();
        await HardwareCollection.RecordStatuses(db, id, DateTime.UtcNow, MetricsEnabled(config) || s.Platform == "synology", default);
        await db.SaveChangesAsync();
        return Results.Ok(row);
    }
}
