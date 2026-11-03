
namespace Homelab.Api;
public static class SpareCompatibility
{
    public static string? Validate(Disk disk, InventoryAsset asset, SpareReviewInput input, DateTime now)
    {
        if (input.RequestId == Guid.Empty)
            return "A review request identifier is required.";
        if (asset.Archived || asset.Type != "spare")
            return "Choose an active spare inventory item.";
        if (disk.MappingConfidence == "ambiguous" || string.IsNullOrWhiteSpace(Replacements.Identity(disk)) || input.ConfirmedIdentity != Replacements.Identity(disk))
            return "Confirm the exact drive serial or WWN; ambiguous identities cannot be reviewed.";
        if (input.Verdict is not ("compatible" or "incompatible" or "unknown"))
            return "Choose a supported compatibility assessment.";
        if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000)
            return "Record evidence, including capacity, interface, sector format and physical fit (up to 4000 characters).";
        if (input.ValidUntilUtc.Kind != DateTimeKind.Utc || input.ValidUntilUtc <= now || input.ValidUntilUtc > now.AddYears(1))
            return "Choose a future review deadline within one year, in UTC.";
        return null;
    }

    public static string Status(SpareCompatibilityReview review, Disk disk, InventoryAsset? asset, DateTime now)
    {
        if (asset is null || asset.Archived || asset.Type != "spare")
            return "spare unavailable";
        if (asset.UpdatedUtc != review.AssetUpdatedUtc)
            return "inventory changed";
        if (disk.MappingConfidence == "ambiguous" || Replacements.Identity(disk) != review.ConfirmedIdentity || disk.Model != review.DiskModel)
            return "identity needs review";
        if (review.ValidUntilUtc <= now)
            return "review expired";
        if (!disk.Present || disk.Lifecycle == "retired")
            return "drive not current";
        if (review.Verdict != "compatible")
            return review.Verdict;
        return asset.Quantity > 0 ? "reviewed compatible" : "no stock recorded";
    }

    public static bool SameSubmission(SpareCompatibilityReview row, SpareReviewInput input) => row.AssetId == input.AssetId && row.AssetUpdatedUtc == input.AssetUpdatedUtc && row.ConfirmedIdentity == input.ConfirmedIdentity && row.Verdict == input.Verdict && row.Evidence == input.Evidence && row.ValidUntilUtc == input.ValidUntilUtc;
}
