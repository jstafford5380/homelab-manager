
namespace Homelab.Api;
public record HardwareReadingInput(Guid? Revision, DateTime TakenUtc, string Kind, int? PeriodMinutes, double? CpuPercent, double? MemoryUsedBytes, double? TemperatureC, double? PowerWatts, string Note);
