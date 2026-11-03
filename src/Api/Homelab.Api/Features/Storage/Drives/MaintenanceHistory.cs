
namespace Homelab.Api;
public static class MaintenanceHistory
{
    public static string? Validate(Disk disk, MaintenanceInput input, DateTime now)
    {
        if (input.RequestId == Guid.Empty)
            return "A request identifier is required for safe retries.";
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

    public static bool SameSubmission(DriveMaintenance record, MaintenanceInput input) => record.RequestId == input.RequestId && record.OccurredUtc == input.OccurredUtc && record.DateConfidence == input.DateConfidence && record.Kind == input.Kind && record.Outcome == input.Outcome && record.Summary == input.Summary && record.Evidence == input.Evidence && record.ConfirmedIdentity == input.ConfirmedIdentity;
}
