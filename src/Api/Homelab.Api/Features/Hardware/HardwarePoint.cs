namespace Homelab.Api;
public record HardwarePoint(DateTime AtUtc, double Value, string Source, Guid? ReadingId = null);
