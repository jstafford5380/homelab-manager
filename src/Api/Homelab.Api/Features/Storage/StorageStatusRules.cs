
namespace Homelab.Api;
public static class StorageStatusRules
{
    public static StorageStatus Drive(bool stale, bool present, string? memberState, long? read, long? write, long? checksum, IReadOnlyList<Finding> findings, bool alert, bool inPool, bool smartEnabled = false, bool? smartPassed = null, bool smartAvailable = false, string? alertReason = null)
    {
        var open = findings.Where(f => f.State != "resolved").ToArray();
        var result = !present ? new StorageStatus("Missing", "warn", "This drive is absent from the latest inventory.") : memberState is "FAULTED" or "UNAVAIL" or "REMOVED" || open.Any(f => f.Severity == "critical") || smartEnabled && smartPassed == false ? new("Act now", "bad", open.FirstOrDefault(f => f.Severity == "critical")?.Title ?? "The latest health evidence reports a fault. Check the drive and your backup before taking action.") : read > 0 || write > 0 || checksum > 0 || open.Any(f => f.Severity is "urgent" or "review") || alert ? new("Watch", "warn", open.FirstOrDefault(f => f.Severity is "urgent" or "review")?.Title ?? (alert ? alertReason ?? "A native NAS alert names this drive. Review the alert before taking action." : "ZFS reported errors on this drive. Check the pool and your backups.")) : smartEnabled && !smartAvailable ? new("Unknown", "neutral", "This drive doesn't provide SMART data.") : !inPool ? new("Spare", "neutral", "This drive is not a member of a known pool.") : string.IsNullOrEmpty(memberState) || memberState is not ("ONLINE" or "AVAIL") || read is null || write is null || checksum is null ? new("Unknown", "neutral", "ZFS did not provide a complete member state and error counters for this drive.") : new StorageStatus("Healthy", "ok", null);
        return stale ? new("Last known: " + result.Label.ToLowerInvariant(), "neutral", "This server is not reporting current data. " + (result.Why ?? "Drive status may have changed since the last check.")) : result;
    }

    public static StorageStatus Pool(bool stale, string state, IEnumerable<StorageStatus> drives)
    {
        var rows = drives.ToArray();
        bool Is(StorageStatus x, string label) => x.Label.Equals(label, StringComparison.OrdinalIgnoreCase) || x.Label.Equals("Last known: " + label, StringComparison.OrdinalIgnoreCase);
        var result = state is "DEGRADED" or "FAULTED" or "UNAVAIL" or "SUSPENDED" or "OFFLINE" ? new StorageStatus("Degraded", "bad", null) : rows.Any(x => Is(x, "Watch") || Is(x, "Act now") || Is(x, "Missing")) ? new("Needs a look", "warn", null) : state != "ONLINE" || rows.Length == 0 || rows.Any(x => Is(x, "Unknown")) ? new("Partly unknown", "neutral", null) : new("Healthy", "ok", null);
        return stale ? new("Last known: " + result.Label.ToLowerInvariant(), "neutral", null) : result;
    }
}
