using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;

public sealed class SpareCompatibilityReview
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public Guid AssetId { get; set; }
    public Guid RequestId { get; set; }
    public DateTime AssetUpdatedUtc { get; set; }
    public string ConfirmedIdentity { get; set; } = "";
    public string DiskModel { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string Verdict { get; set; } = "unknown";
    public string Evidence { get; set; } = "";
    public DateTime ValidUntilUtc { get; set; }
    public DateTime RecordedUtc { get; set; }
    public string Author { get; set; } = "";
}

public record SpareReviewInput(Guid RequestId, Guid AssetId, DateTime AssetUpdatedUtc, string ConfirmedIdentity,
    string Verdict, string Evidence, DateTime ValidUntilUtc);

public static class SpareCompatibility
{
    public static string? Validate(Disk disk, InventoryAsset asset, SpareReviewInput input, DateTime now)
    {
        if (input.RequestId == Guid.Empty) return "A review request identifier is required.";
        if (asset.Archived || asset.Type != "spare") return "Choose an active spare inventory item.";
        if (disk.MappingConfidence == "ambiguous" || string.IsNullOrWhiteSpace(Replacements.Identity(disk)) || input.ConfirmedIdentity != Replacements.Identity(disk))
            return "Confirm the exact drive serial or WWN; ambiguous identities cannot be reviewed.";
        if (input.Verdict is not ("compatible" or "incompatible" or "unknown")) return "Choose a supported compatibility assessment.";
        if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000) return "Record evidence, including capacity, interface, sector format and physical fit (up to 4000 characters).";
        if (input.ValidUntilUtc.Kind != DateTimeKind.Utc || input.ValidUntilUtc <= now || input.ValidUntilUtc > now.AddYears(1))
            return "Choose a future review deadline within one year, in UTC.";
        return null;
    }

    public static string Status(SpareCompatibilityReview review, Disk disk, InventoryAsset? asset, DateTime now)
    {
        if (asset is null || asset.Archived || asset.Type != "spare") return "spare unavailable";
        if (asset.UpdatedUtc != review.AssetUpdatedUtc) return "inventory changed";
        if (disk.MappingConfidence == "ambiguous" || Replacements.Identity(disk) != review.ConfirmedIdentity || disk.Model != review.DiskModel) return "identity needs review";
        if (review.ValidUntilUtc <= now) return "review expired";
        if (!disk.Present || disk.Lifecycle == "retired") return "drive not current";
        if (review.Verdict != "compatible") return review.Verdict;
        return asset.Quantity > 0 ? "reviewed compatible" : "no stock recorded";
    }

    public static bool SameSubmission(SpareCompatibilityReview row, SpareReviewInput input) =>
        row.AssetId == input.AssetId && row.AssetUpdatedUtc == input.AssetUpdatedUtc && row.ConfirmedIdentity == input.ConfirmedIdentity &&
        row.Verdict == input.Verdict && row.Evidence == input.Evidence && row.ValidUntilUtc == input.ValidUntilUtc;

    public static void MapSpareCompatibility(this RouteGroupBuilder api)
    {
        api.MapGet("/disks/{id:guid}/spare-reviews", async (Guid id, long? before, HomelabDb db) => {
            var disk = await db.Disks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (disk is null) return Results.NotFound();
            if (before is <= 0) return Results.BadRequest(new { error = "Use a positive review cursor." });
            var rows = await db.SpareReviews.AsNoTracking().Where(x => x.DiskId == id && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(26).ToListAsync();
            var assetIds = rows.Select(x => x.AssetId).Distinct().ToArray();
            var assets = await db.Assets.AsNoTracking().Where(x => assetIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var latest = await db.SpareReviews.Where(x => x.DiskId == id && assetIds.Contains(x.AssetId)).GroupBy(x => x.AssetId).Select(g => new { AssetId = g.Key, Id = g.Max(x => x.Id) }).ToDictionaryAsync(x => x.AssetId, x => x.Id);
            var now = DateTime.UtcNow;
            return Results.Ok(new { generatedUtc = now, items = rows.Take(25).Select(r => new { review = r,
                status = latest[r.AssetId] != r.Id ? "superseded" : Status(r, disk, assets.GetValueOrDefault(r.AssetId), now) }), nextBefore = rows.Count > 25 ? (long?)rows[24].Id : null });
        });
        api.MapPost("/disks/{id:guid}/spare-reviews", async (Guid id, SpareReviewInput input, HomelabDb db, HttpContext ctx) => {
            var disk = await db.Disks.FindAsync(id); if (disk is null) return Results.NotFound();
            var existing = await db.SpareReviews.SingleOrDefaultAsync(x => x.DiskId == id && x.RequestId == input.RequestId);
            if (existing is not null) return SameSubmission(existing, input) ? Results.Ok(existing) : Results.Conflict(new { error = "This request was already saved with different content. Refresh the review history." });
            var asset = await db.Assets.FindAsync(input.AssetId); if (asset is null) return Results.NotFound();
            if (asset.UpdatedUtc != input.AssetUpdatedUtc) return Results.Conflict(new { error = "Spare inventory changed. Close this form and refresh planning before reviewing it again." });
            var now = DateTime.UtcNow;
            if (Validate(disk, asset, input, now) is {} error) return Results.BadRequest(new { error });
            var row = new SpareCompatibilityReview { DiskId = id, AssetId = asset.Id, RequestId = input.RequestId, AssetUpdatedUtc = asset.UpdatedUtc,
                ConfirmedIdentity = input.ConfirmedIdentity, DiskModel = disk.Model, AssetName = asset.Name, Verdict = input.Verdict, Evidence = input.Evidence,
                ValidUntilUtc = input.ValidUntilUtc, RecordedUtc = now, Author = ctx.User.Identity!.Name! };
            db.SpareReviews.Add(row);
            db.Activity.Add(new() { Actor = row.Author, Action = "Spare compatibility reviewed", Detail = $"Drive {id}; spare {asset.Id}; {row.Verdict}. Manual assessment only; no reservation or NAS action." });
            await db.SaveChangesAsync(); return Results.Ok(row);
        });
    }
}
