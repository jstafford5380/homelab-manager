
namespace Homelab.Api;
public record StorageServer(Guid Id, string Name, string Host, string Freshness, DateTime? LastSuccessUtc, int PoolCount, int DriveCount, long? RawCapacityBytes, int? AttentionCount, NativeAlert[] Alerts, StoragePool[] Pools, StorageDrive[] LooseDrives);
