using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class SynologyHardwareCollector(IServiceScopeFactory scopes, IConfiguration config, IDataProtectionProvider protection, ISynologyHardwareReader reader, WriteCoordinator writes, ILogger<SynologyHardwareCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CollectDue(ct);
            }
            catch (OperationCanceledException)when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning("Synology hardware collection failed ({ErrorType}).", e.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task CollectDue(CancellationToken ct)
    {
        if (!HardwareInventory.SynologyEnabled(config))
            return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        var now = DateTime.UtcNow;
        var servers = await db.HardwareServers.Where(s => s.Platform == "synology" && s.Connected && s.ProtectedCommunity != null && (s.NextAttemptUtc == null || s.NextAttemptUtc <= now)).ToArrayAsync(ct);
        foreach (var s in servers)
        {
            var revision = s.ConfigurationRevision;
            IReadOnlyDictionary<string, string>? data = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                data = await reader.ReadAsync(s.Host, protection.CreateProtector("synology-snmp").Unprotect(s.ProtectedCommunity!), timeout.Token);
            }
            catch (Exception)when (!ct.IsCancellationRequested)
            {
            }

            using var lease = await writes.EnterAsync(ct);
            await db.Entry(s).ReloadAsync(ct);
            if (s.ConfigurationRevision != revision || !s.Connected)
                continue;
            s.NextAttemptUtc = DateTime.UtcNow.AddMinutes(5);
            if (data is not null && data.TryGetValue("model", out var model) && !string.IsNullOrWhiteSpace(model))
            {
                s.Version = data.GetValueOrDefault("version");
                s.LastSuccessUtc = DateTime.UtcNow;
                await Stage(db, s, data, DateTime.UtcNow, ct);
                await db.SaveChangesAsync(ct);
                await HardwareCollection.RecordStatuses(db, s.Id, DateTime.UtcNow, true, ct);
            }

            await db.SaveChangesAsync(ct);
        }
    }

    public static async Task Stage(HomelabDb db, HardwareServer s, IReadOnlyDictionary<string, string> data, DateTime now, CancellationToken ct)
    {
        double? N(string key) => double.TryParse(data.GetValueOrDefault(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
        var total = N("totalKiB") * 1024;
        var free = N("freeKiB") * 1024;
        var cache = (N("cacheKiB") + N("buffersKiB")) * 1024;
        var memoryKnown = total > 0 && free >= 0 && cache >= 0 && free + cache <= total;
        await HardwareCollection.Detected(db, s.Id, "nas-unit", data["model"], s.Version ?? "DSM version unknown", null, null, now, ct);
        if (total > 0)
            await HardwareCollection.Detected(db, s.Id, "memory", $"{total / 1073741824:0.#} GiB memory", "Physical memory reported by SNMP", total, null, now, ct);
        var fans = new[]
        {
            N("systemFan"),
            N("cpuFan")
        };
        if (fans.Any(f => f is 1 or 2))
            await HardwareCollection.Detected(db, s.Id, "cooling", "System fans", "Reported fan state; speeds unknown", null, fans.Any(f => f == 2), now, ct);
        if (N("cpuIdle")is >= 0 and <= 100)
            await HardwareCollection.Detected(db, s.Id, "processor", "Processor", "Model and core counts not reported by this SNMP scalar set", null, null, now, ct);
        var r = new HardwareReading
        {
            ServerId = s.Id,
            Source = "collected",
            ConfigurationRevision = s.ConfigurationRevision,
            TakenUtc = now,
            CpuPercent = N("cpuIdle")is >= 0 and <= 100 ? 100 - N("cpuIdle") : null,
            MemoryUsedBytes = memoryKnown ? total - free - cache : null,
            TotalBytes = total,
            CacheBytes = memoryKnown ? cache : null,
            FreeBytes = memoryKnown ? free : null,
            TemperatureC = N("temperature")is> 0 and <= 200 ? N("temperature") : null
        };
        if (r.CpuPercent is not null || r.MemoryUsedBytes is not null || r.TemperatureC is not null)
        {
            db.HardwareReadings.Add(r);
            s.CollectionStartedUtc ??= now;
            s.LastMetricsUtc = now;
        }
    // Some DSM models do not expose a CPU model in these scalar MIBs. Keep it unknown; entered CPU components can use collected CPU samples.
    }
}
