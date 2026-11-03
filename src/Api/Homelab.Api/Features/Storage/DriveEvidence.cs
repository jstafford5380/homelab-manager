using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class DriveEvidence
{
    public static readonly (string Metric, string Label)[] Catalog = [("scsi.read_uncorrected_errors", "SCSI uncorrected read errors"), ("scsi.write_uncorrected_errors", "SCSI uncorrected write errors"), ("scsi.verify_uncorrected_errors", "SCSI uncorrected verify errors"), ("scsi.grown_defects", "SCSI grown defects"), ("smart.overall_failed", "Overall SMART assessment"), ("ata.pending_sectors", "ATA pending sectors"), ("ata.offline_uncorrectable", "ATA offline uncorrectable"), ("ata.reallocated_sectors", "ATA reallocated sectors"), ("ata.crc_errors", "ATA transport CRC errors"), ("nvme.media_errors", "NVMe media/data-integrity errors"), ("nvme.critical_warning", "NVMe warning flags"), ("nvme.available_spare", "NVMe spare capacity"), ("nvme.available_spare_threshold", "NVMe spare warning threshold"), ("nvme.percentage_used", "NVMe rated endurance used"), ("temperature", "Temperature"), ("power_on_hours", "Power-on hours"), ("power_cycles", "Power cycles")];
    public static MetricEvidence Assess(string metric, string label, Observation? submitted, Observation? observed, DateTime now, TimeSpan maxAge)
    {
        var valid = observed is not null && observed.Metric == metric && observed.Quality == "observed" && ObservationValidation.Validate(observed, now)is null;
        var freshness = !valid ? submitted?.Quality == "unverified" ? "unverified" : "unknown" : now - observed!.ObservedUtc > maxAge ? "stale" : "fresh";
        var explanation = freshness switch
        {
            "fresh" => "Observation time is within the configured evidence window. Freshness does not establish a healthy drive.",
            "stale" => "The most recent observed measurement is older than the configured evidence window. Existing findings remain open.",
            "unverified" => "A reading was submitted without verified measurement evidence. Its receipt time does not establish freshness.",
            _ => "No valid observed measurement is available. This metric may be unsupported or inapplicable to this drive; absence is not zero."
        };
        if (submitted is not null && submitted.Quality != "observed")
            explanation += " The latest submission is " + submitted.Quality + "; any earlier observed evidence is retained separately.";
        if (metric == "smart.overall_failed")
            explanation += " A reported pass does not override adverse readings or prove recovery; no automatic resolution follows a pass.";
        return new(metric, label, freshness, explanation, submitted, valid ? observed : null);
    }

    public static async Task<MetricEvidence[]> ReadAsync(HomelabDb db, Guid diskId, DateTime now, int intervalMinutes, CancellationToken ct = default)
    {
        var metrics = Catalog.Select(x => x.Metric).ToArray();
        var rows = db.Observations.AsNoTracking().Where(x => x.DiskId == diskId && metrics.Contains(x.Metric));
        var latest = await rows.GroupBy(x => x.Metric).Select(g => g.OrderByDescending(x => x.ReceivedUtc).ThenByDescending(x => x.Id).First()).ToListAsync(ct);
        var observed = await rows.Where(x => x.Quality == "observed" && x.Value != null && x.ObservedUtc != null && x.ObservedUtc <= now).GroupBy(x => x.Metric).Select(g => g.OrderByDescending(x => x.ObservedUtc).ThenByDescending(x => x.Id).First()).ToListAsync(ct);
        return Catalog.Select(item => Assess(item.Metric, item.Label, latest.SingleOrDefault(x => x.Metric == item.Metric), observed.SingleOrDefault(x => x.Metric == item.Metric), now, TimeSpan.FromMinutes(intervalMinutes * 3))).ToArray();
    }
}
