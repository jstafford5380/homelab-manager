using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public sealed class DriveAcquisition
{
    public Guid DiskId { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public DateTime? InstalledUtc { get; set; }
    public string InstallationConfidence { get; set; } = "unknown";
    public string InstallationEvidence { get; set; } = "";
    public decimal? PurchaseCost { get; set; }
    public string Currency { get; set; } = "";
    public string CostConfidence { get; set; } = "unknown";
    public string PurchaseEvidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
}
public record DriveAcquisitionInput(Guid? Revision, DateTime? InstalledUtc, string InstallationConfidence,
    string InstallationEvidence, decimal? PurchaseCost, string Currency, string CostConfidence, string PurchaseEvidence);

public static class DriveAcquisitions
{
    public static string? Validate(DriveAcquisitionInput input, DateTime now)
    {
        if (input.InstallationConfidence is not ("unknown" or "estimated" or "documented") || input.CostConfidence is not ("unknown" or "estimated" or "documented"))
            return "Choose unknown, estimated or documented confidence.";
        if (input.InstallationEvidence is null || input.PurchaseEvidence is null || input.InstallationEvidence.Length > 4000 || input.PurchaseEvidence.Length > 4000)
            return "Evidence must be text of at most 4,000 characters; leave it empty if unknown.";
        if (input.InstalledUtc is {} installed && (installed.Kind != DateTimeKind.Utc || installed < DateTime.UnixEpoch || installed > now))
            return "Installation must be a past UTC date on or after January 1, 1970.";
        if (input.InstalledUtc is null ? input.InstallationConfidence != "unknown" : input.InstallationConfidence == "unknown" || string.IsNullOrWhiteSpace(input.InstallationEvidence))
            return "An installation date needs its confidence and evidence; an unknown date must have unknown confidence.";
        if (input.PurchaseCost is < 0 or > 1_000_000_000m) return "Purchase cost must be between zero and 1,000,000,000; leave it blank if unknown.";
        if (input.Currency is null || (input.Currency.Length != 0 && (input.Currency.Length != 3 || !input.Currency.All(char.IsAsciiLetter))))
            return "Currency must be a three-letter code, or blank when cost is unknown.";
        if (input.PurchaseCost is null ? input.CostConfidence != "unknown" || input.Currency.Length != 0 : input.CostConfidence == "unknown" || input.Currency.Length != 3 || string.IsNullOrWhiteSpace(input.PurchaseEvidence))
            return "A purchase cost needs currency, confidence and evidence; an unknown cost must have unknown confidence and no currency.";
        return null;
    }
    public static void MapDriveAcquisitions(this RouteGroupBuilder api)
    {
        api.MapGet("/disks/{id:guid}/acquisition", async (Guid id, HomelabDb db) => {
            if (!await db.Disks.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(new { record = await db.DriveAcquisitions.AsNoTracking().SingleOrDefaultAsync(x => x.DiskId == id) });
        });
        api.MapPut("/disks/{id:guid}/acquisition", async (Guid id, DriveAcquisitionInput input, HomelabDb db, HttpContext context) => {
            if (!await db.Disks.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var record = await db.DriveAcquisitions.SingleOrDefaultAsync(x => x.DiskId == id);
            if (input.Revision != record?.Revision) return Results.Conflict(new { error = "These acquisition notes changed. Your draft was not saved. Reload the saved notes and review them before trying again." });
            if (Validate(input, DateTime.UtcNow) is {} error) return Results.BadRequest(new { error });
            var before = record is null ? null : JsonSerializer.Serialize(record);
            if (record is null) { record = new() { DiskId = id }; db.DriveAcquisitions.Add(record); }
            record.Revision = Guid.NewGuid(); record.InstalledUtc = input.InstalledUtc;
            record.InstallationConfidence = input.InstallationConfidence; record.InstallationEvidence = input.InstallationEvidence;
            record.PurchaseCost = input.PurchaseCost; record.Currency = input.Currency.ToUpperInvariant();
            record.CostConfidence = input.CostConfidence; record.PurchaseEvidence = input.PurchaseEvidence;
            record.Author = context.User.Identity!.Name!; record.RecordedUtc = DateTime.UtcNow;
            db.DiskAnnotations.Add(new() { DiskId = id, Actor = record.Author, BeforeJson = before ?? "null", AfterJson = JsonSerializer.Serialize(record) });
            db.Activity.Add(new() { Actor = record.Author, Action = "Drive acquisition notes updated", Detail = id.ToString() });
            await db.SaveChangesAsync(); return Results.Ok(record);
        });
    }
}
