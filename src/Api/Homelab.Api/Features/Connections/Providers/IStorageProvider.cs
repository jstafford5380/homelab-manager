namespace Homelab.Api;

internal interface IStorageProvider : IIntegrationProvider
{
    string DisplayName { get; }
    IReadOnlyCollection<string> EventCollections { get; }
    Guid[] EventRefreshCandidates();
    Task RetainAsync(IReadOnlyDictionary<Guid, Guid> active, CancellationToken ct);
    IStorageReadSession CreateSession();
}

internal interface IStorageReadSession : IAsyncDisposable
{
    bool RequiresInventoryReconciliation { get; }
    string? ServerCertificateSha256 { get; }
    Task ConnectAsync(NasConnection profile, string key, CancellationToken ct);
    Task<ReportedSystem> ReadSystemAsync(CancellationToken ct);
    Task<IReadOnlyDictionary<string, string>> SubscribeEventsAsync(CancellationToken ct);
    Task<SourceRead<DiskInventory[]>> ReadDisksAsync(CancellationToken ct);
    Task<SourceRead<ReportedPool[]>> ReadPoolsAsync(CancellationToken ct);
    Task<SourceRead<ReportedAlert[]>> ReadAlertsAsync(CancellationToken ct);
    Task<DatasetSnapshot> ReadDatasetsAsync(CancellationToken ct);
    Task<CachedTemperatureSnapshot> ReadTemperaturesAsync(CancellationToken ct);
    Task<ApiPowerOnSnapshot> ReadPowerOnHoursAsync(DiskInventory[] disks, Guid revision, string? previous, CancellationToken ct);
    Task<HardwareReporting> ReadHardwareAsync(bool backfill, CancellationToken ct);
    Task<UpsSnapshot> ReadUpsAsync(CancellationToken ct);
    Task<UpsDevice[]?> ReadUpsDevicesAsync(NasConnection profile, CancellationToken ct);
}

internal sealed record SourceRead<T>(T? Data, string Status) where T : class;
internal sealed record ReportedSystem(string Version, string? Serial, bool SupportedRelease,
    HardwareInventorySample Hardware, PoolSourceContext? PoolContext);
internal sealed record ReportedAlert(string Id, string Level, string Class, string Message, bool Dismissed);
internal sealed record ReportedPool(string Name, string Guid, string Status, object? Topology,
    PoolSnapshot? Snapshot);
internal delegate HardwareReading[] HardwareReporting(Guid serverId, Guid revision, DateTime now, double? totalBytes);
