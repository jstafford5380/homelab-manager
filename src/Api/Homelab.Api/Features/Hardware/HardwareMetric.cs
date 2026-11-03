namespace Homelab.Api;
public record HardwareMetric(string Source, double? Average, double? Latest, double? Peak, double? P95, double? PreviousAverage, int DaysAboveLevel, int ReadingsAboveLevel, int ReadingCount, bool Partial, double? ReviewLevel, HardwareStatus? Status, HardwarePoint[] Series, double? CacheAverage = null, double? FreeAverage = null);
