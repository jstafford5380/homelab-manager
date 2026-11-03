using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class StorageSmart
{
    public static SmartReportPreview? Preview(string? json)
    {
        try
        {
            return json is null ? null : JsonSerializer.Deserialize<SmartReportPreview>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static StorageSmartHealth Health(SmartRead? read, SmartReportPreview? report)
    {
        double? Metric(string metric) => report?.Readings.FirstOrDefault(x => x.Metric == metric && x.Quality == "observed")?.Value;
        return new(report?.OverallSmart?.Quality == "observed" ? report.OverallSmart.Passed : null, report?.ObservedUtc, read?.SkippedAsleep ?? false, read?.Error, Metric("power_on_hours"), Metric("temperature"), Metric("ata.pending_sectors"), Metric("ata.reallocated_sectors"), Metric("nvme.percentage_used"), report?.DeviceInfo?.Quality == "observed" ? report.DeviceInfo.Firmware : null, report?.AtaSelfTests?.FirstOrDefault(x => x.Quality == "observed"));
    }

    public static StorageTrend? Trend(string metric, IReadOnlyList<Observation> observations)
    {
        var points = observations.Where(x => x.Metric == metric && x.Quality == "observed" && x.Value is not null && x.ObservedUtc is not null).OrderBy(x => x.ObservedUtc).ThenBy(x => x.Id).ToArray();
        if (points.Length < 2 || points.Select(x => x.Value).Distinct().Count() < 2)
            return null;
        var segments = new List<List<StorageTrendPoint>>();
        Observation? previous = null;
        foreach (var p in points)
        {
            // Unknown/reset continuity and decreases break the line; never imply repair.
            var continuous = previous is not null && p.ResetEpoch.Length > 0 && previous.ResetEpoch == p.ResetEpoch && previous.Source == p.Source && previous.SourceSchema == p.SourceSchema && previous.Unit == p.Unit && p.ObservedUtc > previous.ObservedUtc && p.Value >= previous.Value;
            if (!continuous)
                segments.Add([]);
            segments[^1].Add(new(p.ObservedUtc!.Value, p.Value!.Value));
            previous = p;
        }

        return new(metric, segments.Select(x => x.ToArray()).ToArray(), "Last 8 weeks. Gaps mark unknown continuity or a counter reset; a reset is not evidence of repair.");
    }
}
