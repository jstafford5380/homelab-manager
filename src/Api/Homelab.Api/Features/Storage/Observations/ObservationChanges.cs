using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class ObservationChanges
{
    public static ObservationChange Compare(Observation current, Observation? previous, DateTime asOfUtc, bool ambiguous = false)
    {
        ObservationChange Result(string status, string explanation, double? difference = null) => new(previous?.Id, previous?.ObservedUtc, status, difference, current.Unit == "%" ? "percentage points" : current.Unit, explanation);
        if (current.Metric is "nvme.critical_warning" or "smart.overall_failed")
            return Result("categorical", "Flags and assessment results are not numeric counter differences. Review the recorded value and its interpretation.");
        if (ambiguous)
            return Result("ambiguous_time", "Multiple records share this or the preceding observation time. No record is silently selected as authoritative.");
        if (current.Quality != "observed" || current.Value is null || ObservationValidation.Validate(current, asOfUtc)is not null)
            return Result("unverified", "This reading lacks valid observed measurement evidence; no difference is calculated.");
        if (previous is null)
            return Result("baseline", "No earlier timestamped record exists in this retained history snapshot. This is not a zero baseline.");
        if (previous.Quality != "observed" || previous.Value is null || ObservationValidation.Validate(previous, asOfUtc)is not null)
            return Result("unverified", "The preceding timestamped reading is missing or unverified. It is not skipped to manufacture continuity.");
        if (current.DiskId is null || current.DiskId != previous.DiskId || current.ConnectionId != previous.ConnectionId || current.Metric != previous.Metric || current.Source != previous.Source || current.SourceSchema != previous.SourceSchema || current.Unit != previous.Unit || current.ObservedUtc <= previous.ObservedUtc)
            return Result("context_changed", "Identity, source, schema, units or observation ordering changed; these readings are not comparable.");
        if (string.IsNullOrWhiteSpace(current.ResetEpoch) || current.ResetEpoch != previous.ResetEpoch)
            return Result("continuity_unknown", "A matching explicit continuity group is required. Unknown continuity, resets and source changes are not treated as uninterrupted counters.");
        var gauge = current.Metric is "temperature" or "nvme.available_spare" or "nvme.available_spare_threshold";
        if (!gauge && current.Value < previous.Value)
            return Result("counter_decreased", "The counter decreased. Reset, replacement or source discontinuity requires review; this is not a negative error rate or proof of repair.");
        var difference = current.Value.Value - previous.Value.Value;
        if (!double.IsFinite(difference) || Math.Abs(difference) > 9007199254740991d)
            return Result("out_of_range", "The difference exceeds the exact numeric range supported by this application.");
        return Result("compared", "Observed net difference between the two recorded times, not a rate or continuous coverage. Undetected resets between samples remain possible; this does not predict failure or establish repair.", difference);
    }

    public static async Task<Dictionary<long, ObservationChange>> ReadAsync(HomelabDb db, Guid diskId, string metric, long ceiling, Observation[] items, DateTime asOfUtc, CancellationToken ct)
    {
        var result = new Dictionary<long, ObservationChange>();
        var source = db.Observations.AsNoTracking().Where(x => x.DiskId == diskId && x.Metric == metric && x.Id <= ceiling);
        foreach (var current in items)
        {
            Observation? previous = null;
            var ambiguous = false;
            if (current.ObservedUtc is { } at && current.Quality == "observed" && current.Value is not null && current.Metric is not ("nvme.critical_warning" or "smart.overall_failed"))
            {
                ambiguous = await source.Where(x => x.ObservedUtc == at).Take(2).CountAsync(ct) > 1;
                // Chronological predecessor, not import-order predecessor; independent of the visible page/window.
                var earlier = await source.Where(x => x.ObservedUtc < at).OrderByDescending(x => x.ObservedUtc).ThenByDescending(x => x.Id).Take(2).ToArrayAsync(ct);
                previous = earlier.FirstOrDefault();
                ambiguous |= earlier.Length == 2 && earlier[0].ObservedUtc == earlier[1].ObservedUtc;
            }

            result[current.Id] = Compare(current, previous, asOfUtc, ambiguous);
        }

        return result;
    }
}
