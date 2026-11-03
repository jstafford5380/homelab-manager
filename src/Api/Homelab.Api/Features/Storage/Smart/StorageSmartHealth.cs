
namespace Homelab.Api;
public record StorageSmartHealth(bool? Passed, DateTime? ReadUtc, bool SkippedAsleep, string? Error, double? PowerOnHours, double? TemperatureC, double? PendingSectors, double? ReallocatedSectors, double? NvmeWearPercent, string? Firmware, AtaSelfTestLog? LastSelfTest);
