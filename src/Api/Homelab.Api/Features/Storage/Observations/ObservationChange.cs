
namespace Homelab.Api;
public record ObservationChange(long? PreviousId, DateTime? PreviousObservedUtc, string Status, double? Difference, string Unit, string Explanation);
