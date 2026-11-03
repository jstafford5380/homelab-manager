using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class HardwareRetention
{
    // Preserve detailed evidence for a year so changed review levels can be evaluated exactly.
    // Hourly averages are also kept for two years. Never expire entered readings or component history.
    public static async Task Summarize(HomelabDb db, DateTime now, CancellationToken ct)
    {
        var cutoff = now.Date.AddDays(-30);
        var rows = await db.HardwareReadings.AsNoTracking().Where(r => r.Source == "collected" && r.TakenUtc < cutoff && r.TakenUtc >= now.AddDays(-366)).OrderBy(r => r.TakenUtc).ToArrayAsync(ct);
        var existing = (await db.HardwareHourly.AsNoTracking().Where(h => h.HourUtc < cutoff).Select(h => new { h.ServerId, h.ConfigurationRevision, h.HourUtc }).ToArrayAsync(ct)).Select(h => (h.ServerId, h.ConfigurationRevision, h.HourUtc)).ToHashSet();
        double? Avg(IEnumerable<double?> values)
        {
            var v = values.OfType<double>().ToArray();
            return v.Length == 0 ? null : v.Average();
        }

        foreach (var g in rows.GroupBy(r => (r.ServerId, r.ConfigurationRevision, Hour: new DateTime(r.TakenUtc.Ticks / TimeSpan.TicksPerHour * TimeSpan.TicksPerHour, DateTimeKind.Utc))))
        {
            if (existing.Contains((g.Key.ServerId, g.Key.ConfigurationRevision, g.Key.Hour)))
                continue;
            db.HardwareHourly.Add(new() { ServerId = g.Key.ServerId, ConfigurationRevision = g.Key.ConfigurationRevision, HourUtc = g.Key.Hour, SampleCount = g.Count(), CpuAverage = Avg(g.Select(r => r.CpuPercent)), AppsAverageBytes = Avg(g.Select(r => r.MemoryUsedBytes)), CacheAverageBytes = Avg(g.Select(r => r.CacheBytes)), FreeAverageBytes = Avg(g.Select(r => r.FreeBytes)), TemperatureAverageC = Avg(g.Select(r => r.TemperatureC)), TemperatureMaxC = g.Max(r => r.TemperatureC) });
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task<int> Expire(HomelabDb db, DateTime now, CancellationToken ct) => await db.HardwareReadings.Where(r => r.Source == "collected" && r.TakenUtc < now.AddDays(-366)).ExecuteDeleteAsync(ct) + await db.HardwareHourly.Where(h => h.HourUtc < now.AddDays(-732)).ExecuteDeleteAsync(ct);
}
