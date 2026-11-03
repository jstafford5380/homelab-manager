
namespace Homelab.Api;
public record DatasetSnapshot(DateTime FetchedUtc, DatasetUsage[] Items)
{
    public string SourceSchema => "truenas-24.10-datasets-v1";
}
