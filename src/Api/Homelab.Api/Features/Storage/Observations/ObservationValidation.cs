namespace Homelab.Api;
public static class ObservationValidation
{
    public static string? CanonicalUnit(string? metric) => metric switch
    {
        "ata.pending_sectors" or "ata.offline_uncorrectable" or "ata.reallocated_sectors" => "sectors",
        "ata.crc_errors" or "nvme.media_errors" or "scsi.read_uncorrected_errors" or "scsi.write_uncorrected_errors" or "scsi.verify_uncorrected_errors" => "errors",
        "nvme.critical_warning" => "bitmask",
        "nvme.available_spare" or "nvme.available_spare_threshold" => "%",
        "nvme.percentage_used" => "% rated endurance",
        "scsi.grown_defects" => "defects",
        "smart.overall_failed" => "flag",
        "power_cycles" => "cycles",
        "temperature" => "°C",
        "power_on_hours" => "hours",
        _ => null
    };
    public static string? Validate(Observation input, DateTime now)
    {
        var unit = CanonicalUnit(input.Metric);
        if (unit is null)
            return "Choose a supported observation metric.";
        if (string.IsNullOrWhiteSpace(input.SourceKey) || input.SourceKey.Length > 200 || string.IsNullOrWhiteSpace(input.Source) || input.Source.Length > 2000)
            return "Provide an evidence identifier (up to 200 characters) and source reference (up to 2,000 characters).";
        if (string.IsNullOrWhiteSpace(input.SourceSchema) || input.SourceSchema.Length > 200 || input.ResetEpoch is null || input.ResetEpoch.Length > 200 || input.MissingReason?.Length > 4000)
            return "Provide a metric schema and bounded continuity metadata. Use an empty continuity group when it is unknown.";
        if (input.Unit != unit)
            return $"Use {unit} for {input.Metric}. Convert the source measurement explicitly before importing it.";
        if (input.Quality is not ("observed" or "unknown" or "unverified"))
            return "Choose observed, unknown or unverified quality.";
        if (input.ObservedUtc is { } observed && (observed.Kind != DateTimeKind.Utc || observed > now || observed < DateTime.UnixEpoch))
            return "Observation time must be in the past, after 1970, as an explicit UTC timestamp ending in Z.";
        if (input.Quality == "observed" && input.ObservedUtc is null)
            return "Observed evidence requires its observation time. Use unverified quality when the time is unknown.";
        if (input.Value is null && (input.Quality == "observed" || string.IsNullOrWhiteSpace(input.MissingReason)))
            return "Missing values require unknown/unverified quality and an explanation; they are never zero.";
        if (input.Quality == "unknown" && input.Value is not null)
            return "Unknown evidence must not contain a numeric value.";
        if (input.Value is { } value)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > 9007199254740991d)
                return "Enter a finite measurement within the exact numeric range supported by this importer (±9,007,199,254,740,991).";
            if (input.Metric == "smart.overall_failed" && value is not (0 or 1))
                return "Overall SMART failure must be 0 (reported passed) or 1 (reported failed).";
            if (input.Metric is "nvme.critical_warning" or "nvme.percentage_used" or "nvme.available_spare" or "nvme.available_spare_threshold")
            {
                var maximum = input.Metric is "nvme.available_spare" or "nvme.available_spare_threshold" ? 100 : 255;
                if (value < 0 || value > maximum || value != Math.Truncate(value))
                    return $"Use a whole number from 0 to {maximum} for {input.Metric}.";
            }

            if (input.Metric == "temperature" && value < -273.15)
                return "Celsius temperatures cannot be below absolute zero. Check the source units.";
            if (input.Metric != "temperature" && value < 0)
                return "Counters and operating hours cannot be negative.";
            if (unit is "sectors" or "errors" or "cycles" or "defects" && (value != Math.Truncate(value) || value > 9007199254740991d))
                return "Counts must be whole numbers no larger than 9,007,199,254,740,991; larger values cannot be represented exactly by this importer.";
        }

        return null;
    }
}
