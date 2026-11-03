
namespace Homelab.Api;
public record ScanReport(Guid ConnectionId, string NasName, string PoolGuid, string PoolName, string Function, string State, DateTime FetchedUtc, DateTime? ValidUntilUtc, string Freshness, string Explanation, double? Percentage, long? Errors, DateTime? ScanStartedUtc, DateTime? ScanEndedUtc, DateTime? ScanPausedUtc, long? ScanRemainingSeconds);
