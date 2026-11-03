namespace Homelab.Api.Features.Storage.Drives.GetMaintenanceHistory;
public sealed record Response(IEnumerable<DriveMaintenance> Items, long? NextBefore);
