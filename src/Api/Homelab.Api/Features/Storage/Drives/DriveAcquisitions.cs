using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class DriveAcquisitions
{
    public static string? Validate(DriveAcquisitionInput input, DateTime now)
    {
        if (input.InstallationConfidence is not ("unknown" or "estimated" or "documented") || input.CostConfidence is not ("unknown" or "estimated" or "documented"))
            return "Choose unknown, estimated or documented confidence.";
        if (input.InstallationEvidence is null || input.PurchaseEvidence is null || input.InstallationEvidence.Length > 4000 || input.PurchaseEvidence.Length > 4000)
            return "Evidence must be text of at most 4,000 characters; leave it empty if unknown.";
        if (input.InstalledUtc is { } installed && (installed.Kind != DateTimeKind.Utc || installed < DateTime.UnixEpoch || installed > now))
            return "Installation must be a past UTC date on or after January 1, 1970.";
        if (input.InstalledUtc is null ? input.InstallationConfidence != "unknown" : input.InstallationConfidence == "unknown" || string.IsNullOrWhiteSpace(input.InstallationEvidence))
            return "An installation date needs its confidence and evidence; an unknown date must have unknown confidence.";
        if (input.PurchaseCost is < 0 or > 1_000_000_000m)
            return "Purchase cost must be between zero and 1,000,000,000; leave it blank if unknown.";
        if (input.Currency is null || (input.Currency.Length != 0 && (input.Currency.Length != 3 || !input.Currency.All(char.IsAsciiLetter))))
            return "Currency must be a three-letter code, or blank when cost is unknown.";
        if (input.PurchaseCost is null ? input.CostConfidence != "unknown" || input.Currency.Length != 0 : input.CostConfidence == "unknown" || input.Currency.Length != 3 || string.IsNullOrWhiteSpace(input.PurchaseEvidence))
            return "A purchase cost needs currency, confidence and evidence; an unknown cost must have unknown confidence and no currency.";
        return null;
    }
}
