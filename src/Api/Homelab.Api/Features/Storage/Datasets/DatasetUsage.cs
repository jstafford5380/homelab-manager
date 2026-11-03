
namespace Homelab.Api;
// Dataset paths are labels, not durable identity. This adapter intentionally does not
// infer renames, removals, pool GUIDs, health, or historical continuity from these names.
public record DatasetUsage(string Name, string Pool, string Type, long? UsedBytes, long? AvailableBytes, long? SnapshotUsedBytes, long? QuotaBytes, long? RefQuotaBytes);
