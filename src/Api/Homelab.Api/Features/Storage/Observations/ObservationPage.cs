
namespace Homelab.Api;
public record ObservationPage(DateTime AsOfUtc, long SnapshotId, Observation[] Items, long? NextBefore, Dictionary<long, ObservationChange> Comparisons);
