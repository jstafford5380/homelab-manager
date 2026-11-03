
namespace Homelab.Api;
public record CapacityEstimate(int WindowDays, string Status, string Explanation, int Samples, int Days, DateTime? FirstUtc, DateTime? LastUtc, long? SizeBytes, long? AllocatedBytes, double? GrowthBytesPerDay, double? DaysTo80Percent, double? Fit);
