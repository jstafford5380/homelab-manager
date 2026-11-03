
namespace Homelab.Api;
public record ApiPowerOnReading(string Device, string Serial, string Model, string? Wwn, string Status, long? Hours, DateTime? ReadUtc = null);
