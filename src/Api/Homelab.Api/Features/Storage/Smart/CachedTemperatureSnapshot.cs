
namespace Homelab.Api;
public record CachedTemperatureSnapshot(DateTime FetchedUtc, CachedTemperature[] Readings)
{
    public string SourceSchema => "truenas-24.10-temperature-cache-v1";
}
