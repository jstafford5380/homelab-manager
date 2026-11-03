
namespace Homelab.Api;
public record ActivityHistoryPage(long SnapshotId, AuditEvent[] Items, long? NextBefore);
