using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class SynologyHardwareCollector(IServiceScopeFactory scopes, IConfiguration config, IDataProtectionProvider protection, ISynologyHardwareReader reader, WriteCoordinator writes, ILogger<SynologyHardwareCollector> logger, ProviderRegistry? providers = null) : BackgroundService
{
    private readonly ProviderRegistry registry = providers ?? new([new SynologyHardwareProvider(reader)]);

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
        var provider = registry.Resolve<IHardwareProvider>(server.Platform);
        var revision = server.ConfigurationRevision;
        HardwareSample? data = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            data = await provider.ReadAsync(server.Host, protection.CreateProtector("synology-snmp")
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
        if (data is null)
        {
            await db.SaveChangesAsync(ct);
            return false;
        }

        var now = DateTime.UtcNow;
        server.Version = data.Inventory.Version;
        server.LastSuccessUtc = now;
        await Stage(db, server, data, now, ct);
        await db.SaveChangesAsync(ct);
        await HardwareCollection.RecordStatuses(db, server.Id, now, true, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static Task Stage(HomelabDb db, HardwareServer server, IReadOnlyDictionary<string, string> data, DateTime now, CancellationToken ct) =>
        Stage(db, server, SynologyHardwareProvider.Parse(data, server.Version), now, ct);

    private static async Task Stage(HomelabDb db, HardwareServer server, HardwareSample data, DateTime now, CancellationToken ct)
    {
        await HardwareCollection.StageComponents(db, server.Id, data.Inventory, now, ct);
        if (data.Reading is { } reading)
        {
            reading.ServerId = server.Id;
            reading.ConfigurationRevision = server.ConfigurationRevision;
            reading.TakenUtc = now;
            db.HardwareReadings.Add(reading);
            server.CollectionStartedUtc ??= now;
            server.LastMetricsUtc = now;
        }
    }
}
