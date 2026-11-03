using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;

public sealed class UpsPowerSample
{
    public Guid ServerId { get; set; }
    public string DeviceKey { get; set; } = "";
    public DateTime BucketUtc { get; set; }
    public double LoadPercent { get; set; }
    public double? NominalVa { get; set; }
}

public record UpsMonthlyPower(string DeviceKey, double MedianWatts, int SampleCount, int DaysWithSamples, double? EstimatedCostUsd)
{
    internal static async Task Stage(HomelabDb db, Guid serverId, UpsDevice[] devices, DateTime now, CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(5).Ticks;
        var bucket = new DateTime(now.Ticks / interval * interval, DateTimeKind.Utc);
        var saved = await db.UpsPowerSamples.Where(s => s.ServerId == serverId && s.BucketUtc == bucket)
            .Select(s => s.DeviceKey).ToArrayAsync(ct);
        foreach (var device in devices)
        {
            if (device.Key is { } key && device.Load is { Value: >= 0 and <= 1000 } load && !saved.Contains(key))
                db.UpsPowerSamples.Add(new() { ServerId = serverId, DeviceKey = key, BucketUtc = bucket,
                    LoadPercent = load.Value, NominalVa = device.NominalVa is > 0 and <= 1_000_000 ? device.NominalVa : null });
        }
    }

    internal static async Task<UpsMonthlyPower[]> Read(HomelabDb db, Guid serverId, HardwareComponent[] components,
        UpsDevice[] devices, double? rate, DateTime now, CancellationToken ct)
    {
        var keys = devices.Select(device => device.Key).OfType<string>().ToHashSet();
        var linked = components.Where(c => c.RetiredUtc is null && c.Kind == "ups" && c.UpsPowerFactor is > 0 and <= 1 &&
            c.UpsDeviceKey is { } key && keys.Contains(key)).ToArray();
        if (linked.Length == 0)
            return [];
        var linkedKeys = linked.Select(c => c.UpsDeviceKey!).ToArray();
        var cutoff = now.AddDays(-30);
        var samples = await db.UpsPowerSamples.AsNoTracking().Where(s => s.ServerId == serverId &&
            linkedKeys.Contains(s.DeviceKey) && s.BucketUtc >= cutoff && s.BucketUtc <= now).ToArrayAsync(ct);
        var byDevice = samples.ToLookup(s => s.DeviceKey);
        var result = new List<UpsMonthlyPower>();
        foreach (var c in linked)
        {
            var usable = byDevice[c.UpsDeviceKey!].Where(s => (s.NominalVa ?? c.UpsNominalVa) is > 0).ToArray();
            var watts = usable.Select(s => (s.NominalVa ?? c.UpsNominalVa)!.Value *
                (s.LoadPercent / 100) * c.UpsPowerFactor!.Value).Order().ToArray();
            if (watts.Length == 0)
                continue;
            var median = (watts[(watts.Length - 1) / 2] + watts[watts.Length / 2]) / 2;
            result.Add(new(c.UpsDeviceKey!, median, watts.Length, usable.Select(s => s.BucketUtc.Date).Distinct().Count(),
                rate is { } price ? median / 1000 * 720 * price : null));
        }
        return result.ToArray();
    }
}
