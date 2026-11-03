namespace Homelab.Api;
public static class HealthRules
{
    public static RuleResult? Evaluate(Observation current, Observation? previous)
    {
        if (current.Value is null || current.ObservedUtc is null || current.Quality != "observed")
            return null;
        if (previous?.ObservedUtc >= current.ObservedUtc)
            return null;
        var comparable = previous is not null && previous.Quality == "observed" && current.Unit == previous.Unit && current.Source == previous.Source && current.SourceSchema == previous.SourceSchema && current.ResetEpoch.Length > 0 && current.ResetEpoch == previous.ResetEpoch;
        var increased = comparable && previous?.Value is { } old && current.Value > old;
        var reset = previous?.Value is { } prior && current.Value < prior;
        return current.Metric switch
        {
            "scsi.read_uncorrected_errors" or "scsi.write_uncorrected_errors" or "scsi.verify_uncorrected_errors" when increased && current.Unit == "errors" => new("scsi-uncorrected-increase-v1:" + current.Metric, "urgent", "SCSI uncorrected error count increased", $"{current.Metric} changed from {previous!.Value} to {current.Value} between {previous.ObservedUtc:O} and {current.ObservedUtc:O}.", "Review native error details and independent recovery evidence. Investigate device and path context before replacement; this counter does not identify affected files or predict a failure date."),
            "scsi.grown_defects" when increased && current.Unit == "defects" => new("scsi-grown-defects-increase-v1", "urgent", "SCSI grown-defect count increased", $"Grown-defect elements changed from {previous!.Value} to {current.Value} between {previous.ObservedUtc:O} and {current.ObservedUtc:O}.", "Review native SCSI evidence, recovery readiness and replacement planning. Defect-list elements are not ATA sectors; this trend does not predict a failure date."),
            "smart.overall_failed" when current.Value == 1 && current.Unit == "flag" => new("smart-overall-failed-v1", "urgent", "Drive reported a failed overall SMART assessment", $"Overall SMART assessment reported failure at {current.ObservedUtc:O}. Source: {current.Source}. This is historical report evidence, not a predicted failure date.", "Review current native drive details and independent recovery readiness promptly. Assess replacement compatibility and budget. A later passed result alone does not establish repair or resolve this finding."),
            "nvme.critical_warning" when current.Value is> 0 and <= 255 && current.Value == Math.Truncate(current.Value.Value) && current.Unit == "bitmask" => new("nvme-warning-v1", ((int)current.Value.Value & 8) != 0 ? "critical" : "urgent", "NVMe device reports warning flags", $"Warning mask 0x{(int)current.Value.Value:X2} at {current.ObservedUtc:O}: {NvmeWarningDescription((int)current.Value.Value)}.", "Review the device's native warning details and recovery readiness. Warning flags are device-reported conditions, not a failure probability."),
            "nvme.percentage_used" when current.Value is >= 100 and <= 255 && current.Value == Math.Truncate(current.Value.Value) && current.Unit == "% rated endurance" => new("nvme-endurance-v1", "review", "NVMe rated-endurance estimate reached", $"Manufacturer endurance estimate: {current.Value}% used at {current.ObservedUtc:O}. A value of 255 represents more than 254%.", "Review manufacturer guidance, workload, recovery readiness and replacement budget. Reaching rated endurance does not by itself establish failure or a replacement deadline."),
            "ata.pending_sectors" or "ata.offline_uncorrectable" when current.Value > 0 => new("media-present-v1:" + current.Metric, "urgent", "Media observations need review", $"{current.Metric}: {current.Value} {current.Unit} at {current.ObservedUtc:O}." + (reset ? " Counter decreased; reset or source change requires review." : ""), "Review disk evidence and backup coverage; consider a compatible replacement. This does not predict a failure date."),
            "ata.reallocated_sectors" or "nvme.media_errors" when increased => new("media-increase-v1:" + current.Metric, "urgent", "Media counter increased", $"{current.Metric} changed from {previous!.Value} to {current.Value} between {previous.ObservedUtc:O} and {current.ObservedUtc:O}.", "Review the trend and recovery readiness before planning replacement."),
            "ata.crc_errors" when increased => new("transport-increase-v1", "review", "Transport errors increased", $"CRC observations changed from {previous!.Value} to {current.Value}.", "Inspect cable, controller, and power-path evidence. This alone does not establish failed media."),
            _ => null
        };
    }

    private static string NvmeWarningDescription(int flags)
    {
        var labels = new List<string>();
        foreach (var(bit, label)in new[]
        {
            (1, "spare capacity below device threshold"),
            (2, "temperature outside device threshold"),
            (4, "reliability degraded"),
            (8, "media placed in read-only mode"),
            (16, "volatile memory backup failed"),
            (32, "persistent memory region read-only or unreliable")
        }

        )
            if ((flags & bit) != 0)
                labels.Add(label);
        if ((flags & ~63) != 0)
            labels.Add($"unrecognized flags 0x{flags & ~63:X2}; interpretation unknown");
        return string.Join("; ", labels);
    }
}
