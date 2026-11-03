
namespace Homelab.Api;
public interface ISynologyHardwareReader
{
    Task<IReadOnlyDictionary<string, string>> ReadAsync(string host, string community, CancellationToken ct);
}
