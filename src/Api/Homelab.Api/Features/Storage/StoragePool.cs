
namespace Homelab.Api;
public record StoragePool(string Guid, string Name, StorageStatus Status, string Layout, int MemberCount, int? ToleratedFailures, long? UsedBytes, long? SizeBytes, PoolSnapshot? Scan, PoolCapacityOutlook? CapacityOutlook, DatasetUsage[] Datasets, string DatasetStatus, StorageDrive[] Drives, int UnmappedMembers);
