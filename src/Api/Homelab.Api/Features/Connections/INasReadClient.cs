using System.Text.Json;

namespace Homelab.Api;
// Deliberately no public arbitrary-command API: callers must pass the fixed read manifest.
public interface INasReadClient : IAsyncDisposable
{
    string? ServerCertificateSha256 => null;

    bool IsConnected => false;

    bool RequiresInventoryReconciliation => false;

    long EventSequence => 0;

    long InventoryEventSequence => 0;

    Task<IReadOnlyDictionary<string, string>> SubscribeEventsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    Task ConnectAsync(NasConnection profile, string key, CancellationToken ct);
    Task<JsonElement> ReadAsync(string method, CancellationToken ct);
    Task<JsonElement> ReadUpsMetricsAsync(CancellationToken ct) => throw new NasReadException("unsupported");
    Task<JsonElement> ReadHardwareMetricsAsync(bool backfill, CancellationToken ct) => throw new NasReadException("unsupported");
    Task<JsonElement> ReadSmartAttributesAsync(string disk, CancellationToken ct) => throw new NasReadException("unsupported");
}
