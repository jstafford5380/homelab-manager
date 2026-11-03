using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Homelab.Api;
// One exclusive lease per NAS; healthy authenticated transports survive collection cycles.
public sealed class NasReadClientFactory : INasReadClientFactory, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Slot> slots = new();
    private readonly Func<INasReadClient> create;
    private int disposed;
    public NasReadClientFactory() : this(() => new TrueNasClient())
    {
    }

    public NasReadClientFactory(Func<INasReadClient> create) => this.create = create;
    private sealed class Slot
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public INasReadClient? Client;
        public Guid Revision;
        public long ConsumedEvents, ConsumedInventoryEvents;
        public string Stamp = "";
    }

    public INasReadClient Create() => new Lease(this);
    public Guid[] EventRefreshCandidates() => slots.Where(pair => pair.Value.Client is { } client && (!client.IsConnected || client.EventSequence > Interlocked.Read(ref pair.Value.ConsumedEvents))).Select(pair => pair.Key).ToArray();
    private static string Stamp(NasConnection profile, string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { profile.ConfigurationRevision, profile.ApiUri, profile.ConnectHost, profile.ConnectPort, profile.TrustedCaPem, profile.CertificateSha256, key }))));
    public async Task RetainAsync(IReadOnlyDictionary<Guid, Guid> active, CancellationToken ct)
    {
        foreach (var pair in slots)
        {
            var slot = pair.Value;
            await slot.Gate.WaitAsync(ct);
            try
            {
                if (!active.TryGetValue(pair.Key, out var revision) || revision != slot.Revision)
                {
                    if (slot.Client is not null)
                        await slot.Client.DisposeAsync();
                    slot.Client = null;
                    slot.Stamp = "";
                }
            }
            finally
            {
                slot.Gate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        foreach (var slot in slots.Values)
        {
            await slot.Gate.WaitAsync();
            try
            {
                if (slot.Client is not null)
                    await slot.Client.DisposeAsync();
                slot.Client = null;
            }
            finally
            {
                slot.Gate.Release();
            }
        }
    }

    private sealed class Lease(NasReadClientFactory owner) : INasReadClient
    {
        private Slot? held;
        private bool closed;
        private long eventsAtStart, inventoryEventsAtStart;
        public string? ServerCertificateSha256 => held?.Client?.ServerCertificateSha256;
        public bool IsConnected => held?.Client?.IsConnected == true;
        public long EventSequence => held?.Client?.EventSequence ?? 0;
        public long InventoryEventSequence => held?.Client?.InventoryEventSequence ?? 0;
        public bool RequiresInventoryReconciliation { get; private set; }

        public async Task ConnectAsync(NasConnection profile, string key, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(closed || Volatile.Read(ref owner.disposed) != 0, this);
            if (held is not null)
                throw new InvalidOperationException("Lease is already connected.");
            var slot = owner.slots.GetOrAdd(profile.Id, _ => new());
            await slot.Gate.WaitAsync(ct);
            held = slot;
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref owner.disposed) != 0, this);
                var stamp = Stamp(profile, key);
                if (slot.Client?.IsConnected == true && slot.Stamp == stamp)
                {
                    eventsAtStart = slot.Client.EventSequence;
                    inventoryEventsAtStart = slot.Client.InventoryEventSequence;
                    RequiresInventoryReconciliation = inventoryEventsAtStart > slot.ConsumedInventoryEvents;
                    return;
                }

                if (slot.Client is not null)
                    await slot.Client.DisposeAsync();
                slot.Client = null;
                var client = owner.create();
                try
                {
                    await client.ConnectAsync(profile, key, ct);
                }
                catch
                {
                    await client.DisposeAsync();
                    throw;
                }

                slot.Client = client;
                slot.Stamp = stamp;
                slot.Revision = profile.ConfigurationRevision;
                slot.ConsumedEvents = 0;
                slot.ConsumedInventoryEvents = 0;
                eventsAtStart = 0;
                inventoryEventsAtStart = 0;
                RequiresInventoryReconciliation = true;
            }
            catch
            {
                held = null;
                slot.Gate.Release();
                throw;
            }
        }

        public Task<IReadOnlyDictionary<string, string>> SubscribeEventsAsync(CancellationToken ct) => (held?.Client ?? throw new InvalidOperationException("Lease is not connected.")).SubscribeEventsAsync(ct);
        public Task<JsonElement> ReadAsync(string method, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            // Keep the fixed manifest enforced even if a different transport is supplied in tests.
            if (!TrueNasClient.ReadMethods.Contains(method))
                throw new InvalidOperationException("Method is outside the read allowlist.");
            return (held?.Client ?? throw new InvalidOperationException("Lease is not connected.")).ReadAsync(method, ct);
        }

        public Task<JsonElement> ReadUpsMetricsAsync(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return (held?.Client ?? throw new InvalidOperationException("Lease is not connected.")).ReadUpsMetricsAsync(ct);
        }

        public Task<JsonElement> ReadHardwareMetricsAsync(bool backfill, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return (held?.Client ?? throw new InvalidOperationException("Lease is not connected.")).ReadHardwareMetricsAsync(backfill, ct);
        }

        public Task<JsonElement> ReadSmartAttributesAsync(string disk, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (!TrueNasClient.ValidSmartDisk(disk))
                throw new InvalidOperationException("Invalid disk name.");
            return (held?.Client ?? throw new InvalidOperationException("Lease is not connected.")).ReadSmartAttributesAsync(disk, ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (closed)
                return;
            closed = true;
            var slot = held;
            held = null;
            if (slot is null)
                return;
            try
            {
                Interlocked.Exchange(ref slot.ConsumedEvents, eventsAtStart);
                Interlocked.Exchange(ref slot.ConsumedInventoryEvents, inventoryEventsAtStart);
                if (slot.Client is not null && !slot.Client.IsConnected)
                {
                    await slot.Client.DisposeAsync();
                    slot.Client = null;
                    slot.Stamp = "";
                }
            }
            finally
            {
                slot.Gate.Release();
            }
        }
    }
}
