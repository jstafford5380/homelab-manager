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

    private readonly SemaphoreSlim collectionGate = new(1, 1);

    public async Task CollectDue(CancellationToken ct)
    {
        if (!HardwareInventory.SynologyEnabled(config) || !await collectionGate.WaitAsync(0, ct))
            return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            var now = DateTime.UtcNow;
            var servers = await db.HardwareServers.Where(s => s.Platform == "synology" && s.Connected &&
                s.ProtectedCommunity != null && (s.NextAttemptUtc == null || s.NextAttemptUtc <= now)).ToArrayAsync(ct);
            foreach (var server in servers)
                await CollectAsync(db, server, ct);
        }
        finally
        {
            collectionGate.Release();
        }
    }

    public async Task<IResult> RefreshNowAsync(Guid id, CancellationToken ct)
    {
        if (!HardwareInventory.SynologyEnabled(config))
            return Results.NotFound();
        if (!await collectionGate.WaitAsync(0, ct))
            return Results.Conflict(new { error = "Synology collection is already running. Try again shortly." });

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            var server = await db.HardwareServers.SingleOrDefaultAsync(s => s.Id == id && s.Platform == "synology", ct);
            if (server is null)
                return Results.NotFound();
            if (!server.Connected || server.ProtectedCommunity is null)
                return Results.BadRequest(new { error = "Connect this server in Settings before refreshing." });
            if (await CollectAsync(db, server, ct))
                return Results.NoContent();
            return Results.Json(new { error = "Synology did not return hardware inventory." }, statusCode: 502);
        }
        finally
        {
            collectionGate.Release();
        }
    }

    private async Task<bool> CollectAsync(HomelabDb db, HardwareServer server, CancellationToken ct)
    {
        var revision = server.ConfigurationRevision;
        IReadOnlyDictionary<string, string>? data = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            data = await reader.ReadAsync(server.Host, protection.CreateProtector("synology-snmp")
                .Unprotect(server.ProtectedCommunity!), timeout.Token);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
        }

        using var lease = await writes.EnterAsync(ct);
        await db.Entry(server).ReloadAsync(ct);
        if (db.Entry(server).State == EntityState.Detached || server.ConfigurationRevision != revision || !server.Connected)
            return false;
        server.NextAttemptUtc = DateTime.UtcNow.AddMinutes(5);
        if (data is null || !data.TryGetValue("model", out var model) || string.IsNullOrWhiteSpace(model))
        {
            await db.SaveChangesAsync(ct);
            return false;
        }

        var now = DateTime.UtcNow;
        server.Version = data.GetValueOrDefault("version");
        server.LastSuccessUtc = now;
        await Stage(db, server, data, now, ct);
        await db.SaveChangesAsync(ct);
        await HardwareCollection.RecordStatuses(db, server.Id, now, true, ct);
        await db.SaveChangesAsync(ct);
        return true;
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
