
namespace Homelab.Api;
public static class DiskMetadataRules
{
    public static string? ValidateMetadata(Disk disk, DiskMetadata input)
    {
        if (input.BayVerified && (string.IsNullOrWhiteSpace(input.Bay) || string.IsNullOrWhiteSpace(input.BayEvidence) || input.ConfirmedSerial != disk.Serial || string.IsNullOrWhiteSpace(disk.Serial) || disk.MappingConfidence == "ambiguous"))
            return "Bay verification requires unambiguous identity, matching serial confirmation, a bay label, and evidence.";
        if (input.PurchaseDateConfidence is not ("unknown" or "estimated" or "documented") || input.Lifecycle is not ("installed" or "spare" or "retired"))
            return "Invalid date confidence or lifecycle.";
        if (input.PurchasedUtc is { } purchased && (purchased.Kind != DateTimeKind.Utc || purchased < DateTime.UnixEpoch) || input.WarrantyEndUtc is { } warranty && (warranty.Kind != DateTimeKind.Utc || warranty < DateTime.UnixEpoch))
            return "Purchase and warranty dates must include a UTC timezone and be on or after January 1, 1970.";
        if (input.PurchasedUtc > DateTime.UtcNow || input.PurchasedUtc is not null && input.PurchaseDateConfidence == "unknown" || input.PurchasedUtc is null && input.PurchaseDateConfidence != "unknown")
            return "Specify whether an entered purchase date is estimated or documented.";
        if (input.Notes is null)
            return "Notes must be text; leave them empty if unknown.";
        if (input.Notes.Length > 10000 || input.Bay?.Length > 100 || input.BayEvidence?.Length > 2000)
            return "Metadata is too long.";
        return null;
    }
}
