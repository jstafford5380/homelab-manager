
namespace Homelab.Api;
public record CachedTemperature(string DeviceName, int? Celsius, string? MissingReason);
