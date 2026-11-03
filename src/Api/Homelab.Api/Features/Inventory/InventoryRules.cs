using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class InventoryRules
{
    public static string? ValidateAsset(InventoryAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Name) || asset.Name.Length > 200 || asset.Type is not ("host" or "service" or "enclosure" or "controller" or "backup target" or "spare" or "power" or "network") || asset.Criticality is not ("low" or "normal" or "important" or "critical") || asset.Verification is not ("unverified" or "verified") || asset.Quantity is < 0 or > 10000 || asset.EstimatedUnitCost < 0 || asset.Currency is null || asset.Currency.Length != 3 || !asset.Currency.All(char.IsAsciiLetter))
            return "Check name, asset type, importance, verification, quantity, and budget.";
        if (!string.IsNullOrWhiteSpace(asset.ManagementUrl) && (!Uri.TryCreate(asset.ManagementUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            return "Management links must be HTTP(S) URLs without embedded credentials.";
        foreach (var(label, value, limit)in new (string, string? , int)[]
        {
            ("Purpose", asset.Purpose, 2000),
            ("Owner", asset.Owner, 200),
            ("Location", asset.Location, 500),
            ("Version", asset.Version, 200),
            ("Management link", asset.ManagementUrl, 2000),
            ("Runbook", asset.Runbook, 50000),
            ("Documentation", asset.Documentation, 10000),
            ("Compatibility notes", asset.CompatibilityNotes, 5000)
        }

        )
            if (value is null || value.Length > limit)
                return $"{label} must be text with at most {limit} characters; leave it empty if unknown.";
        return null;
    }
}
