using System.Text.Json;

namespace Homelab.Api;

internal static class TrueNasHardware
{
    public static string? Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()is { Length: > 0 and <= 500 } s ? s : null;
    public static double? Number(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;

    internal static HardwareInventorySample ParseInventory(JsonElement system)
    {
        var components = new List<DetectedHardware>();
        var version = Text(system, "version");
        var model = Text(system, "model");
        var memory = Number(system, "physmem");
        var product = Text(system, "system_product");
        if (model is not null)
            components.Add(new("processor", model, $"{Number(system, "physical_cores")?.ToString() ?? "Unknown"} physical cores · {Number(system, "cores")?.ToString() ?? "Unknown"} logical cores"));
        if (memory > 0)
            components.Add(new("memory", $"{memory / 1073741824:0.#} GiB memory", "Usable physical memory reported by system.info", memory));
        if (product is not null)
            components.Add(new("nas-unit", product, (Text(system, "system_manufacturer") ?? "System product") + " · OS " + (version ?? "unknown")));
        return new(version, Number(system, "uptime_seconds"), components);
    }

    public static HardwareReading[] Normalize(JsonElement data, Guid serverId, Guid revision, DateTime now, double? total)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 4)
            throw new InvalidDataException("Invalid reporting graphs.");
        var output = new Dictionary<long, HardwareReading>();
        var arc = new Dictionary<long, double>();
        var free = new Dictionary<long, double>();
        // TrueNAS can return both endpoints of an hourly series at one-second resolution.
        const int maxGraphPoints = 3601;
        foreach (var graph in data.EnumerateArray())
        {
            if (graph.ValueKind != JsonValueKind.Object)
                continue;
            var name = Text(graph, "name");
            if (name is not ("cpu" or "memory" or "arcsize" or "cputemp"))
                continue;
            if (!graph.TryGetProperty("legend", out var legend) || legend.ValueKind != JsonValueKind.Array || !graph.TryGetProperty("data", out var points) || points.ValueKind != JsonValueKind.Array || points.GetArrayLength() > maxGraphPoints)
                continue;
            var keys = legend.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : null).ToArray();
            var buckets = new Dictionary<long, List<double>>();
            foreach (var row in points.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != keys.Length || keys.Length == 0 || row[0].ValueKind != JsonValueKind.Number || !row[0].TryGetDouble(out var seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > new DateTimeOffset(now).ToUnixTimeSeconds() || seconds < new DateTimeOffset(now.AddDays(-1)).ToUnixTimeSeconds())
                    continue;
                var bucket = (long)seconds / 300 * 300;
                // Only complete intervals are saved, so overlapping reporting reads never inflate coverage.
                if (bucket + 300 > new DateTimeOffset(now).ToUnixTimeSeconds())
                    continue;
                double? N(string key)
                {
                    var index = Array.IndexOf(keys, key);
                    return index > 0 && row[index].ValueKind == JsonValueKind.Number && row[index].TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
                }

                // Reporting converts nulls to zeros. All-zero rows are ambiguous and cannot establish use or health.
                if (row.EnumerateArray().Skip(1).All(v => v.ValueKind != JsonValueKind.Number || v.GetDouble() == 0))
                    continue;
                double? value = name switch
                {
                    "cpu" => N("idle")is { } idle && idle is >= 0 and <= 100 ? 100 - idle : null,
                    "memory" => N("free")is { } f && f >= 0 ? f * 1048576 : null,
                    "arcsize" => N("arc_size")is { } a && a >= 0 ? a * 1048576 : null,
                    _ => row.EnumerateArray().Skip(1).Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).Where(v => double.IsFinite(v) && v is> 0 and <= 200).Cast<double?>().Max()};
                if (value is { } v)
                {
                    if (!buckets.TryGetValue(bucket, out var values))
                        buckets[bucket] = values = [];
                    values.Add(v);
                }
            }

            foreach (var(bucket, values)in buckets)
            {
                if (!output.TryGetValue(bucket, out var r))
                    output[bucket] = r = new()
                    {
                        ServerId = serverId,
                        ConfigurationRevision = revision,
                        Source = "collected",
                        TakenUtc = DateTimeOffset.FromUnixTimeSeconds(bucket).UtcDateTime,
                        TotalBytes = total
                    };
                // Temperature retains the hottest sensor/sample in a five-minute interval for review evidence.
                if (name == "cpu")
                    r.CpuPercent = values.Average();
                else if (name == "cputemp")
                    r.TemperatureC = values.Max();
                else if (name == "arcsize")
                    arc[bucket] = values.Average();
                else
                    free[bucket] = values.Average();
            }
        }

        foreach (var(bucket, r)in output)
            if (total is> 0 && arc.TryGetValue(bucket, out var cache) && free.TryGetValue(bucket, out var f) && cache + f <= total)
            {
                r.MemoryUsedBytes = total - cache - f;
                r.CacheBytes = cache;
                r.FreeBytes = f;
            }

        return output.Values.Where(r => r.CpuPercent is not null || r.MemoryUsedBytes is not null || r.TemperatureC is not null).ToArray();
    }
}
