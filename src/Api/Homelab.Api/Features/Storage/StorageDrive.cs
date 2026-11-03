
namespace Homelab.Api;
public record StorageDrive(Disk Disk, string Title, StorageStatus Status, string? PoolGuid, string? PoolName, string? MemberState, long? ReadErrors, long? WriteErrors, long? ChecksumErrors, bool BayConfirmed, int? TemperatureC, DateTime? TemperatureFetchedUtc, DriveAcquisition? Acquisition, PlanningItem? Plan, StorageSmartHealth? Smart = null, StorageDriveGroup? Group = null, string? TemperatureNote = null, StoragePowerOnHours? ApiPowerOnHours = null);
