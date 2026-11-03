
namespace Homelab.Api;
public interface INasReadClientFactory
{
    INasReadClient Create();
    Guid[] EventRefreshCandidates() => [];
    Task RetainAsync(IReadOnlyDictionary<Guid, Guid> active, CancellationToken ct) => Task.CompletedTask;
}
