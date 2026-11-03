
namespace Homelab.Api;
public record PoolCapacityOutlook(Guid ConnectionId, string PoolGuid, string PoolName, double? UsedFraction, double ThresholdFraction, DateTime? ProjectedDateAtThreshold, int BasisDays, string Confidence, string Status, string Explanation, DateTime? LastSampleUtc, DateTime? BasisStartedUtc, int SampledDays, double? Fit);
