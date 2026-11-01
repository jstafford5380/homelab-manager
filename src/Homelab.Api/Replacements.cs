using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;

public sealed class DiskReplacement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OldDiskId { get; set; }
    public Guid NewDiskId { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string DateConfidence { get; set; } = "documented";
    public string PoolGuid { get; set; } = "";
    public string MembershipEvidence { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string OldIdentity { get; set; } = "";
    public string NewIdentity { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public DateTime? VoidedUtc { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedBy { get; set; }
}
public record ReplacementInput(Guid OldDiskId, Guid NewDiskId, DateTime OccurredUtc, string DateConfidence,
    string PoolGuid, string MembershipEvidence, string Reason, string Evidence, string ConfirmedOldIdentity, string ConfirmedNewIdentity);
public record VoidReplacementInput(string Reason);

public static class Replacements
{
    public static string Identity(Disk disk) => !string.IsNullOrWhiteSpace(disk.Serial) ? disk.Serial : disk.Wwn ?? "";
    public static string? Validate(Disk oldDisk, Disk newDisk, ReplacementInput input, DateTime now)
    {
        if (oldDisk.Id != input.OldDiskId || newDisk.Id != input.NewDiskId || oldDisk.Id == newDisk.Id || oldDisk.ConnectionId != newDisk.ConnectionId)
            return "Choose two different physical drives from the same NAS.";
        if (Identity(oldDisk) == Identity(newDisk) || oldDisk.MappingConfidence == "ambiguous" || newDisk.MappingConfidence == "ambiguous" || string.IsNullOrWhiteSpace(Identity(oldDisk)) || string.IsNullOrWhiteSpace(Identity(newDisk)))
            return "Resolve ambiguous or missing drive identity before recording a replacement.";
        if (input.ConfirmedOldIdentity != Identity(oldDisk) || input.ConfirmedNewIdentity != Identity(newDisk))
            return "Retype each drive's serial (or WWN when no serial is available) to confirm the link.";
        if (input.OccurredUtc.Kind != DateTimeKind.Utc || input.OccurredUtc > now || input.OccurredUtc < new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) || input.DateConfidence is not ("documented" or "estimated"))
            return "Enter a past replacement time with a UTC offset and indicate whether it is documented or estimated.";
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000 || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 10000 || string.IsNullOrWhiteSpace(input.MembershipEvidence) || input.MembershipEvidence.Length > 4000 || input.PoolGuid is null || input.PoolGuid.Length > 100)
            return "Provide a reason, operator evidence and pool/vdev membership context within the supported lengths. State explicitly when membership is unknown.";
        return null;
    }
    public static DiskReplacement Create(Disk oldDisk, Disk newDisk, ReplacementInput input, string author, DateTime now)
    {
        if (Validate(oldDisk, newDisk, input, now) is {} error) throw new ArgumentException(error);
        return new() { OldDiskId = oldDisk.Id, NewDiskId = newDisk.Id, OccurredUtc = input.OccurredUtc, DateConfidence = input.DateConfidence,
            PoolGuid = input.PoolGuid, MembershipEvidence = input.MembershipEvidence, Reason = input.Reason, Evidence = input.Evidence,
            OldIdentity = Identity(oldDisk), NewIdentity = Identity(newDisk), Author = author, RecordedUtc = now };
    }
    public static void MapReplacements(this RouteGroupBuilder api)
    {
        api.MapGet("/disks/{id:guid}/replacements", async (Guid id, HomelabDb db) => {
            if (!await db.Disks.AnyAsync(d => d.Id == id)) return Results.NotFound();
            return Results.Ok(await db.Replacements.AsNoTracking().Where(r => r.OldDiskId == id || r.NewDiskId == id).OrderByDescending(r => r.OccurredUtc).ToListAsync());
        });
        api.MapPost("/replacements", async (ReplacementInput input, HomelabDb db, HttpContext context) => {
            var oldDisk = await db.Disks.FindAsync(input.OldDiskId); var newDisk = await db.Disks.FindAsync(input.NewDiskId);
            if (oldDisk is null || newDisk is null) return Results.BadRequest(new { error = "Both physical drives must already exist in inventory." });
            var now = DateTime.UtcNow;
            if (Validate(oldDisk, newDisk, input, now) is {} error) return Results.BadRequest(new { error });
            if (await db.Replacements.AnyAsync(r => r.OldDiskId == input.OldDiskId && r.NewDiskId == input.NewDiskId && r.OccurredUtc == input.OccurredUtc && r.VoidedUtc == null))
                return Results.Conflict(new { error = "This replacement has already been recorded." });
            var record = Create(oldDisk, newDisk, input, context.User.Identity!.Name!, now);
            db.Replacements.Add(record);
            db.Activity.Add(new() { Actor = record.Author, Action = "Replacement history recorded locally", Detail = record.Id.ToString() });
            await db.SaveChangesAsync(); return Results.Ok(record);
        });
        api.MapPost("/replacements/{id:guid}/void", async (Guid id, VoidReplacementInput input, HomelabDb db, HttpContext context) => {
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000) return Results.BadRequest(new { error = "Explain the correction in up to 2,000 characters." });
            var record = await db.Replacements.FindAsync(id); if (record is null) return Results.NotFound();
            if (record.VoidedUtc != null) return Results.Conflict(new { error = "This record is already marked incorrect." });
            record.VoidedUtc = DateTime.UtcNow; record.VoidReason = input.Reason; record.VoidedBy = context.User.Identity!.Name!;
            db.Activity.Add(new() { Actor = record.VoidedBy, Action = "Replacement history marked incorrect", Detail = record.Id.ToString() });
            await db.SaveChangesAsync(); return Results.Ok(record);
        });
    }
}
