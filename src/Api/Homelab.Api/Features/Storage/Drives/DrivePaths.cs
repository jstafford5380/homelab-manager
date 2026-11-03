
namespace Homelab.Api;
public static class DrivePaths
{
    public static string? Identity(Disk disk) => !string.IsNullOrWhiteSpace(disk.Serial) ? disk.Serial : IdentityCandidate.NormalizeWwn(disk.Wwn);
    public static string? Validate(Disk disk, InventoryAsset asset, DrivePathInput input, DateTime now)
    {
        if (input.RequestId == Guid.Empty || input.Evidence is null || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000)
            return "Record relationship evidence in 1–4000 characters.";
        if (Identity(disk)is not { } identity || identity != input.ConfirmedIdentity || disk.MappingConfidence == "ambiguous")
            return "Confirm this drive's exact, unambiguous serial or WWN.";
        if (asset.Type is not ("controller" or "enclosure" or "power"))
            return "Choose a controller, enclosure or power asset.";
        if (input.Attached && (asset.Archived || !disk.Present || disk.Lifecycle == "retired"))
            return "An attached relationship requires an active asset and a currently present, non-retired drive.";
        if (input.ValidUntilUtc.Kind != DateTimeKind.Utc || input.ValidUntilUtc <= now || input.ValidUntilUtc > now.AddYears(1))
            return "Set a UTC review expiry within the next year.";
        return null;
    }

    public static string Status(DrivePathReview row, Disk disk, InventoryAsset asset, bool latest, DateTime now) => !latest ? "superseded" : !row.Attached ? "ended" : asset.Archived ? "asset unavailable" : asset.UpdatedUtc != row.AssetUpdatedUtc ? "asset changed" : Identity(disk) != row.ConfirmedIdentity || disk.Model != row.DiskModel || disk.MappingConfidence == "ambiguous" ? "identity needs review" : !disk.Present || disk.Lifecycle == "retired" ? "drive not current" : row.ValidUntilUtc <= now ? "review expired" : "recorded relationship";
    public static bool Same(DrivePathReview row, DrivePathInput input) => row.AssetId == input.AssetId && row.AssetUpdatedUtc == input.AssetUpdatedUtc && row.ConfirmedIdentity == input.ConfirmedIdentity && row.Attached == input.Attached && row.Evidence == input.Evidence && row.ValidUntilUtc == input.ValidUntilUtc;
}
