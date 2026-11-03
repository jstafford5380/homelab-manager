using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class HardwareMetrics
{
    public static readonly string[] Kinds = ["processor", "memory", "motherboard", "power-supply", "ups", "cooling", "storage-controller", "network-card", "graphics-card", "case", "nas-unit", "other"];
    public static string Group(string kind) => kind is "processor" or "memory" or "motherboard" ? "Compute" : kind is "power-supply" or "ups" or "cooling" ? "Power & cooling" : "Expansion & chassis";
    public static HardwareSlot[] Slots(HardwareComponent c)
    {
        try
        {
            return JsonSerializer.Deserialize<HardwareSlot[]>(c.SlotsJson) ?? [];
        }
        catch (JsonException)
        {
            return[];
        }
    }

    public static double? EnteredTotal(HardwareComponent c)
    {
        var slots = Slots(c);
        return slots.Length == 0 && c.SolderedBytes is null ? null : slots.Sum(s => s.SizeBytes ?? 0) + (c.SolderedBytes ?? 0);
    }

    public static HardwareStatus Status(bool stale, string kind, bool missing, bool? fanFailed, HardwareMetric? use, HardwareMetric? heat, int days = 30, string? savedStatus = null, string? savedWhy = null)
    {
        if (stale)
            return new("Last known: " + (savedStatus ?? "Not monitored").ToLowerInvariant(), "neutral", savedWhy ?? "No current evidence is available.");
        if (missing || fanFailed == true)
            return new("Act now", "bad", missing ? "Reported usable memory is below the modules you entered. Kernel and firmware reservations can explain small differences; check the entered inventory and installed modules before concluding that a module is missing." : "The server reports a failed fan.");
        if (heat?.DaysAboveLevel > 0)
            return new("Watch", "warn", $"Temperature exceeded your {heat.ReviewLevel:0.#} °C review level on {heat.DaysAboveLevel} days. Hottest {heat.Peak:0.#} °C. Check cooling and airflow.");
        var limit = days <= 7 ? 1 : 3;
        if (use is not null && (use.Source == "entered" ? use.ReadingsAboveLevel >= 2 && use.ReadingsAboveLevel * 2 >= use.ReadingCount : use.DaysAboveLevel >= limit))
            return new("Near its limit", "info", use.Source == "entered" ? $"{use.ReadingsAboveLevel} of {use.ReadingCount} entered readings were above your review level. Nothing here points to a fault; review whether this part has enough capacity." : $"Use exceeded your review level for the sustained duration on {use.DaysAboveLevel} days. Nothing here points to a fault; review whether this part has enough capacity.");
        if (use?.Source is "collected" or "entered" || heat?.Source is "collected" or "entered" || fanFailed == false)
            return new("Healthy", "ok", "Available measurements are within your review levels.");
        return new("Not monitored", "warn", kind == "cooling" ? "Fan speeds aren’t collected. CPU temperature is the best sign of how cooling is doing." : "Nothing measures this component.");
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var rows = values.OfType<double>().ToArray();
        return rows.Length == 0 ? null : rows.Average();
    }

    private static double? Value(HardwareReading r, string metric) => metric switch
    {
        "cpu" => r.CpuPercent,
        "memory" => r.MemoryUsedBytes,
        "temperature" => r.TemperatureC,
        _ => r.PowerWatts
    };
    public static HardwareMetric Metric(IReadOnlyList<HardwareReading> rows, string metric, DateTime end, int days, double? level, int sustainedMinutes, DateTime? collectionStarted)
    {
        var start = end.AddDays(-days);
        var selected = rows.Where(r => r.TakenUtc > start && r.TakenUtc <= end && Value(r, metric)is not null).OrderBy(r => r.TakenUtc).ToArray();
        var collected = selected.Where(r => r.Source == "collected").GroupBy(r => r.TakenUtc.Ticks / TimeSpan.TicksPerMinute / 5).Select(g => g.Last()).ToArray();
        var entered = selected.Where(r => r.Source == "entered").ToArray();
        var basis = collected.Length > 0 ? collected : entered;
        var source = collected.Length > 0 ? "collected" : entered.Length > 0 ? "entered" : "none";
        var partial = source == "collected" && (collectionStarted is null || collectionStarted > start.AddMinutes(5));
        var values = basis.Select(r => Value(r, metric)!.Value).Order().ToArray();
        double? ReviewValue(HardwareReading r) => metric == "memory" ? r.TotalBytes > 0 ? r.MemoryUsedBytes / r.TotalBytes * 100 : null : Value(r, metric);
        // Each distinct five-minute interval contributes at most five minutes. Gaps do not count as sustained use.
        var above = basis.Where(r => level is not null && ReviewValue(r) > level).ToArray();
        var daysAbove = above.GroupBy(r => r.TakenUtc.Date).Count(g => metric == "temperature" || g.Select(r => r.TakenUtc.Ticks / TimeSpan.TicksPerMinute / 5).Distinct().Count() * 5 >= sustainedMinutes);
        var previous = rows.Where(r => r.Source == "collected" && r.TakenUtc > start.AddDays(-days) && r.TakenUtc <= start && Value(r, metric)is not null).ToArray();
        var fullPrevious = collectionStarted <= start.AddDays(-days).AddMinutes(5) && previous.Select(r => r.TakenUtc.Ticks / TimeSpan.TicksPerMinute / 5).Distinct().Count() >= days * 288 * 0.99;
        // At most 288 collected chart buckets. Entered dots remain independent and are never folded into lines or averages.
        var bucketTicks = Math.Max(TimeSpan.TicksPerMinute * 5, TimeSpan.FromDays(days).Ticks / 288);
        var points = collected.GroupBy(r => (r.TakenUtc.Ticks - start.Ticks) / bucketTicks).Select(g => new HardwarePoint(new DateTime(start.Ticks + g.Key * bucketTicks, DateTimeKind.Utc), g.Average(r => Value(r, metric)!.Value), "collected")).Concat(entered.Select(r => new HardwarePoint(r.TakenUtc, Value(r, metric)!.Value, "entered", r.Id))).OrderBy(p => p.AtUtc).ToArray();
        var result = new HardwareMetric(source, values.Length == 0 || partial ? null : values.Average(), basis.LastOrDefault()is { } latest ? Value(latest, metric) : null, values.Length == 0 ? null : values[^1], values.Length == 0 ? null : values[(int)Math.Ceiling(values.Length * .95) - 1], fullPrevious ? Average(previous.Select(r => Value(r, metric))) : null, source == "entered" ? above.Select(r => r.TakenUtc.Date).Distinct().Count() : daysAbove, entered.Count(r => level is not null && ReviewValue(r) > level), entered.Length, partial, level, null, points, metric == "memory" ? Average(basis.Select(r => r.CacheBytes)) : null, metric == "memory" ? Average(basis.Select(r => r.FreeBytes)) : null);
        var status = Status(false, "processor", false, null, metric is "cpu" or "memory" ? result : null, metric == "temperature" ? result : null, days);
        return result with
        {
            Status = status.Label is "Watch" or "Near its limit" ? status : null
        };
    }

    public static HardwareCoverage Coverage(HardwareReading[] rows, DateTime end, int days, DateTime? started)
    {
        var start = end.AddDays(-days);
        var times = rows.Where(r => r.Source == "collected" && r.TakenUtc > start && r.TakenUtc <= end).Select(r => r.TakenUtc).Distinct().Order().ToArray();
        var gaps = new List<HardwareGap>();
        var cursor = started > start ? started.Value : start;
        foreach (var time in times)
        {
            if (time - cursor >= TimeSpan.FromMinutes(20))
                gaps.Add(new(cursor, time));
            cursor = time;
        }

        if (times.Length > 0 && end - cursor >= TimeSpan.FromMinutes(20))
            gaps.Add(new(cursor, end));
        return new(times.Select(t => t.Ticks / TimeSpan.TicksPerMinute / 5).Distinct().Count(), days * 288, times.FirstOrDefault() == default ? null : times[0], started, days, times.Select(t => t.Date).Distinct().Count(), gaps.ToArray(), times.Any(t => t < end.AddDays(-30)));
    }

    public static async Task<HardwareDetail> Detail(HomelabDb db, HardwareServer server, DateTime now, int days, bool metricsEnabled, CancellationToken ct = default)
    {
        var connection = server.ConnectionId is { } id ? await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        var fresh = connection is not null ? StoragePage.Freshness(connection, now) : !server.Connected ? "Not connected" : server.LastSuccessUtc is { } success && now - success <= TimeSpan.FromMinutes(15) ? "Reporting" : "Out of date";
        if (connection is not null && connection.ConfigurationRevision != server.ConfigurationRevision)
            fresh = "Out of date";
        if (metricsEnabled && server.LastMetricsUtc is { } metricsAt && now - metricsAt > TimeSpan.FromMinutes(15) && fresh == "Reporting")
            fresh = "Out of date";
        var stale = fresh is not ("Reporting" or "Not connected" or "No collection yet");
        var end = stale && (metricsEnabled ? server.LastMetricsUtc ?? server.LastSuccessUtc : server.LastSuccessUtc)is { } last ? last : now;
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new();
        var rows = await db.HardwareReadings.AsNoTracking().Where(r => r.ServerId == server.Id && r.DeletedUtc == null && r.TakenUtc > end.AddDays(-Math.Max(30, days * 2)) && r.TakenUtc <= end && (r.Source == "entered" || metricsEnabled && (server.Platform != "synology" || server.Connected) && r.ConfigurationRevision == server.ConfigurationRevision)).OrderBy(r => r.TakenUtc).ToArrayAsync(ct);
        var components = await db.HardwareComponents.AsNoTracking().Where(c => c.ServerId == server.Id).ToArrayAsync(ct);
        var componentIds = components.Select(c => c.Id).ToArray();
        var history = await db.HardwareEvents.AsNoTracking().Where(e => componentIds.Contains(e.ComponentId)).OrderByDescending(e => e.AtUtc).ToArrayAsync(ct);
        var total = (server.Platform != "synology" || server.Connected ? components.FirstOrDefault(c => c.Kind == "memory" && c.DetectedKey is not null && c.RetiredUtc == null)?.DetectedTotalBytes : null) ?? components.Where(c => c.Kind == "memory" && c.RetiredUtc == null).Select(EnteredTotal).FirstOrDefault(x => x is not null);
        foreach (var r in rows.Where(r => r.Source == "entered"))
            r.TotalBytes ??= total;
        HardwareMetric M(string key, int range) => Metric(rows, key, end, range, key == "cpu" ? settings.HardwareCpuPercent : key == "memory" ? settings.HardwareMemoryPercent : key == "temperature" ? settings.HardwareTemperatureC : null, settings.HardwareSustainedMinutes, server.CollectionStartedUtc);
        var cpu = M("cpu", days);
        var memory = M("memory", days);
        var temperature = M("temperature", days);
        var power = M("power", days);
        var ups = metricsEnabled && connection is not null && connection.ConfigurationRevision == server.ConfigurationRevision
            ? StoragePage.Read<UpsSnapshot>(server.UpsSnapshotJson)
            : null;
        var views = components.Select(c =>
        {
            var partRows = rows.Where(r => (c.InstalledUtc is null || r.TakenUtc >= c.InstalledUtc) && (c.RetiredUtc is null || r.TakenUtc <= c.RetiredUtc)).ToArray();
            var partStarted = c.InstalledUtc > server.CollectionStartedUtc ? c.InstalledUtc : server.CollectionStartedUtc;
            HardwareMetric Part(string key, int range) => Metric(partRows, key, end, range, key == "cpu" ? settings.HardwareCpuPercent : key == "memory" ? settings.HardwareMemoryPercent : settings.HardwareTemperatureC, settings.HardwareSustainedMinutes, partStarted);
            var useKey = c.Kind == "processor" ? "cpu" : c.Kind == "memory" ? "memory" : null;
            var hasHeat = c.Kind == (server.Platform == "synology" ? "nas-unit" : "processor");
            var partUse = useKey is null ? null : Part(useKey, days);
            var partHeat = hasHeat ? Part("temperature", days) : null;
            var entered = EnteredTotal(c);
            var missing = (server.Platform != "synology" || server.Connected) && c.DetectedTotalBytes is { } detected && entered is { } expected && detected < expected;
            var status = c.RetiredUtc is not null ? new HardwareStatus("Replaced", "neutral", "This component is retired; its history is preserved.") : Status(stale, c.Kind, missing, server.Platform == "synology" && !server.Connected ? null : c.FanFailed, useKey is null ? null : Part(useKey, 30), hasHeat ? Part("temperature", 30) : null, 30, c.LastStatus, c.LastWhy);
            if (c.RetiredUtc is null && c.Kind == "ups" && c.UpsDeviceKey is { } upsKey)
            {
                var device = ups?.Devices?.SingleOrDefault(device => device.Key == upsKey);
                var readings = new[] { device?.Load, device?.Charge, device?.Runtime }.OfType<UpsReading>().ToArray();
                status = !metricsEnabled
                    ? new("Not monitored", "warn", "Automatic hardware collection is off.")
                    : readings.Any(reading => !stale && reading.ObservedUtc <= now && reading.ObservedUtc >= now.AddMinutes(-15))
                        ? new("Reporting", "ok", "The linked UPS returned recent telemetry.")
                        : readings.Length > 0
                            ? new("Last known: reporting", "neutral", "The linked UPS has older telemetry.")
                            : new("Readings unavailable", "neutral", "No readings are available for the linked UPS.");
            }
            if (c.RetiredUtc is null && c.Kind == "nas-unit" && c.DetectedKey is not null && status.Label == "Not monitored" &&
                server.LastSuccessUtc is not null && (connection is { Enabled: true } && connection.ConfigurationRevision == server.ConfigurationRevision ||
                    server.Platform == "synology" && server.Connected))
                status = new("Reporting", "ok", "The NAS is reporting system inventory. Detected changes such as OS version are recorded in component history.");
            return new HardwareComponentView(c, string.IsNullOrWhiteSpace(c.Name) ? c.DetectedName ?? c.Kind : c.Name, c.DetectedName is null ? "You entered" : !string.IsNullOrEmpty(c.Name + c.Spec + c.Model + c.Notes) || entered is not null ? "Detected + entered" : "Detected", Group(c.Kind), status, entered, Slots(c), history.Where(e => e.ComponentId == c.Id).Take(100).ToArray(), partUse, partHeat);
        }).ToArray();
        var active = views.Where(c => c.Record.RetiredUtc == null).ToArray();
        var summary = new HardwareServerView(server.Id, connection?.Name ?? server.Name, connection is not null && Uri.TryCreate(connection.ApiUri, UriKind.Absolute, out var uri) ? uri.Host : server.Host, server.Platform, server.Version, fresh, stale && server.LastSuccessUtc is not null ? end : server.LastSuccessUtc, server.UptimeSeconds, active.Length, stale ? null : active.Count(c => c.Status.Label is "Watch" or "Act now"), stale ? null : active.Count(c => c.Status.Label == "Near its limit"));
        var monthlyPower = ups?.Devices is { Length: > 0 } devices
            ? await UpsMonthlyPower.Read(db, server.Id, components, devices, settings.ElectricityRateUsdPerKwh, now, ct)
            : [];
        var lastCollection = connection?.LastAttemptUtc ?? server.LastSuccessUtc;
        return new(summary, Coverage(rows, end, days, server.CollectionStartedUtc), cpu, memory, temperature, power, total, active, rows.Where(r => r.Source == "entered" && r.TakenUtc > end.AddDays(-days)).ToArray(), settings.HardwareCpuPercent, settings.HardwareMemoryPercent, settings.HardwareTemperatureC, settings.HardwareSustainedMinutes, views.Where(c => c.Record.RetiredUtc != null).ToArray(), ups,
            lastCollection is { } timestamp ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc) : null, monthlyPower, settings.ElectricityRateUsdPerKwh);
    }
}
