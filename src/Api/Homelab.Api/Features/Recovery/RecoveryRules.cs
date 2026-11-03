
namespace Homelab.Api;
public static class RecoveryRules
{
    internal static IResult StalePolicy() => Results.Conflict(new { error = "This protection policy changed since you opened it. Your draft has not been saved. Close this form and reopen the current policy to review its scope before submitting again." });
    public static string? ValidatePolicy(RecoveryPolicy input)
    {
        if (string.IsNullOrWhiteSpace(input.Asset) || string.IsNullOrWhiteSpace(input.Destination) || string.IsNullOrWhiteSpace(input.Scope) || input.RpoHours is < 1 or > 8760 || input.RtoHours is < 1 or > 8760 || input.Mechanism is not ("local snapshot" or "replication" or "independent backup" or "offline/off-site copy") || input.Independence is not ("unverified" or "shared" or "verified"))
            return "Enter asset, destination, scope, valid objectives, mechanism and independence.";
        foreach (var(label, value, limit)in new (string, string? , int)[]
        {
            ("Asset", input.Asset, 200),
            ("Destination", input.Destination, 2000),
            ("Scope", input.Scope, 10000),
            ("Exclusions", input.Exclusions, 10000),
            ("Retention", input.Retention, 2000),
            ("Schedule", input.Schedule, 2000),
            ("Destination capacity", input.DestinationCapacity, 4000),
            ("Restore requirements", input.RestoreRequirements, 10000),
            ("Failure domains", input.FailureDomains, 10000),
            ("Runbook", input.Runbook, 50000),
            ("Key custody reference", input.KeyCustodyReference, 2000),
            ("Evidence", input.Evidence, 10000),
            ("Importance", input.Importance, 200)
        }

        )
            if (value is null || value.Length > limit)
                return $"{label} must be text with at most {limit} characters; leave optional fields empty if unknown.";
        return null;
    }
}
