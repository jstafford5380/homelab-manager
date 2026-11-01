using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public sealed class DriveMaintenance
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public Guid RequestId { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string DateConfidence { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string ConfirmedIdentity { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public string SourceSchema { get; set; } = "manual-maintenance-v1";
    public DateTime? VoidedUtc { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedBy { get; set; }
}
public record MaintenanceInput(Guid RequestId, DateTime OccurredUtc, string DateConfidence, string Kind, string Outcome, string Summary, string Evidence, string ConfirmedIdentity);
public record VoidMaintenanceInput(string Reason);
public static class MaintenanceHistory
{
    public static string? Validate(Disk disk, MaintenanceInput input, DateTime now)
    {
        if (input.RequestId == Guid.Empty) return "A request identifier is required for safe retries.";
        if (input.OccurredUtc.Kind != DateTimeKind.Utc || input.OccurredUtc < DateTime.UnixEpoch || input.OccurredUtc > now || input.DateConfidence is not ("documented" or "estimated"))
            return "Enter a past UTC time and indicate whether it is documented or estimated.";
        if (input.Kind is not ("inspection" or "cabling" or "firmware" or "relocation" or "other") || input.Outcome is not ("completed" or "incomplete" or "unknown"))
            return "Choose a supported maintenance category and outcome.";
        if (string.IsNullOrWhiteSpace(input.Summary) || input.Summary.Length > 2000 || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000)
            return "Describe the work in up to 2,000 characters and its evidence in up to 4,000 characters.";
        var identity = Replacements.Identity(disk);
        if (disk.MappingConfidence == "ambiguous" || string.IsNullOrWhiteSpace(identity) || input.ConfirmedIdentity != identity)
            return "Resolve the drive identity and retype its exact serial (or WWN when no serial is available).";
        return null;
    }
    public static bool SameSubmission(DriveMaintenance record, MaintenanceInput input) => record.RequestId == input.RequestId &&
        record.OccurredUtc == input.OccurredUtc && record.DateConfidence == input.DateConfidence && record.Kind == input.Kind &&
        record.Outcome == input.Outcome && record.Summary == input.Summary && record.Evidence == input.Evidence && record.ConfirmedIdentity == input.ConfirmedIdentity;
    public static void MapMaintenance(this RouteGroupBuilder api)
    {
        api.MapGet("/disks/{id:guid}/maintenance", async (Guid id, long? before, int? days, HomelabDb db, CancellationToken ct) => {
            if (!await db.Disks.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            if (before is <= 0 || days is not (null or 0 or 1 or 7 or 30)) return Results.BadRequest();
            var cutoff = days is null or 0 ? DateTime.UnixEpoch : DateTime.UtcNow.AddDays(-days.Value);
            var rows = await db.Maintenance.AsNoTracking().Where(x => x.DiskId == id && x.OccurredUtc >= cutoff && (before == null || x.Id < before))
                .OrderByDescending(x => x.Id).Take(26).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(25), nextBefore = rows.Count > 25 ? (long?)rows[24].Id : null });
        });
        api.MapPost("/disks/{id:guid}/maintenance", async (Guid id, MaintenanceInput input, HomelabDb db, HttpContext context) => {
            var disk = await db.Disks.FindAsync(id); if (disk is null) return Results.NotFound();
            var existing = await db.Maintenance.SingleOrDefaultAsync(x => x.DiskId == id && x.RequestId == input.RequestId);
            if (existing is not null) return SameSubmission(existing, input) ? Results.Ok(existing) : Results.Conflict(new { error = "This request was already recorded with different details. Reload the history before recording another event." });
            if (Validate(disk, input, DateTime.UtcNow) is {} error) return Results.BadRequest(new { error });
            var record = new DriveMaintenance { DiskId = id, RequestId = input.RequestId, OccurredUtc = input.OccurredUtc,
                DateConfidence = input.DateConfidence, Kind = input.Kind, Outcome = input.Outcome, Summary = input.Summary,
                Evidence = input.Evidence, ConfirmedIdentity = input.ConfirmedIdentity, Author = context.User.Identity!.Name!, RecordedUtc = DateTime.UtcNow };
            db.Maintenance.Add(record);
            db.Activity.Add(new() { Actor = record.Author, Action = "Drive maintenance recorded", Detail = $"{id}: {input.Kind}; manual evidence, no NAS action" });
            await db.SaveChangesAsync(); return Results.Ok(record);
        });
        api.MapPost("/disks/{id:guid}/maintenance/{recordId:long}/void", async (Guid id, long recordId, VoidMaintenanceInput input, HomelabDb db, HttpContext context) => {
            var record = await db.Maintenance.SingleOrDefaultAsync(x => x.Id == recordId && x.DiskId == id);
            if (record is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000) return Results.BadRequest(new { error = "Record why this entry is incorrect, in up to 2,000 characters." });
            if (record.VoidedUtc is not null) return Results.Conflict(new { error = "This entry has already been marked incorrect. Its original evidence is retained." });
            record.VoidedUtc = DateTime.UtcNow; record.VoidReason = input.Reason; record.VoidedBy = context.User.Identity!.Name!;
            db.Activity.Add(new() { Actor = record.VoidedBy, Action = "Drive maintenance marked incorrect", Detail = record.Id.ToString() });
            await db.SaveChangesAsync(); return Results.Ok(record);
        });
    }
}
