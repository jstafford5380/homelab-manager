namespace Homelab.Api;

internal interface IHardwareProvider : IIntegrationProvider
{
    Task<HardwareSample?> ReadAsync(string host, string credential, CancellationToken ct);
}

internal sealed record HardwareSample(HardwareInventorySample Inventory, HardwareReading? Reading);

internal sealed class SynologyHardwareProvider(ISynologyHardwareReader reader) : IHardwareProvider
{
    public string ProviderId => "synology";

    public async Task<HardwareSample?> ReadAsync(string host, string credential, CancellationToken ct)
    {
        var data = await reader.ReadAsync(host, credential, ct);
        return data is not null && data.TryGetValue("model", out var model) && !string.IsNullOrWhiteSpace(model)
            ? Parse(data, data.GetValueOrDefault("version"))
            : null;
    }

    internal static HardwareSample Parse(IReadOnlyDictionary<string, string> data, string? version)
    {
        double? N(string key) => double.TryParse(data.GetValueOrDefault(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
        var total = N("totalKiB") * 1024;
        var free = N("freeKiB") * 1024;
        var cache = (N("cacheKiB") + N("buffersKiB")) * 1024;
        var memoryKnown = total > 0 && free >= 0 && cache >= 0 && free + cache <= total;
        var components = new List<DetectedHardware> { new("nas-unit", data["model"], version ?? "DSM version unknown") };
        if (total > 0)
            components.Add(new("memory", $"{total / 1073741824:0.#} GiB memory", "Physical memory reported by SNMP", total, null));
        var fans = new[]
        {
            N("systemFan"),
            N("cpuFan")
        };
        if (fans.Any(f => f is 1 or 2))
            components.Add(new("cooling", "System fans", "Reported fan state; speeds unknown", null, fans.Any(f => f == 2)));
        if (N("cpuIdle")is >= 0 and <= 100)
            components.Add(new("processor", "Processor", "Model and core counts not reported by this SNMP scalar set", null, null));
        var r = new HardwareReading
        {
            Source = "collected",
            CpuPercent = N("cpuIdle")is >= 0 and <= 100 ? 100 - N("cpuIdle") : null,
            MemoryUsedBytes = memoryKnown ? total - free - cache : null,
            TotalBytes = total,
            CacheBytes = memoryKnown ? cache : null,
            FreeBytes = memoryKnown ? free : null,
            TemperatureC = N("temperature")is> 0 and <= 200 ? N("temperature") : null
        };
        return new(new(version, null, components),
            r.CpuPercent is not null || r.MemoryUsedBytes is not null || r.TemperatureC is not null ? r : null);
    }
}
