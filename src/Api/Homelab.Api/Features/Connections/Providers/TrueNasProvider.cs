using System.Text.Json;

namespace Homelab.Api;

internal sealed class TrueNasProvider(INasReadClientFactory clients) : IStorageProvider
{
    public const string Id = "truenas";
    public string ProviderId => Id;
    public string DisplayName => "TrueNAS";
    public IReadOnlyCollection<string> EventCollections => TrueNasClient.EventCollections;
    public Guid[] EventRefreshCandidates() => clients.EventRefreshCandidates();
    public Task RetainAsync(IReadOnlyDictionary<Guid, Guid> active, CancellationToken ct) => clients.RetainAsync(active, ct);
    public IStorageReadSession CreateSession() => new Session(clients.Create());

    private sealed class Session(INasReadClient client) : IStorageReadSession
    {
        public bool RequiresInventoryReconciliation => client.RequiresInventoryReconciliation;
        public string? ServerCertificateSha256 => client.ServerCertificateSha256;
        public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => client.ConnectAsync(profile, key, ct);
        public ValueTask DisposeAsync() => client.DisposeAsync();
        public Task<IReadOnlyDictionary<string, string>> SubscribeEventsAsync(CancellationToken ct) => client.SubscribeEventsAsync(ct);

        public async Task<ReportedSystem> ReadSystemAsync(CancellationToken ct)
        {
            var started = DateTime.UtcNow;
            var data = await client.ReadAsync("system.info", ct);
            var finished = DateTime.UtcNow;
            var version = Text(data, "version");
            var serial = NasIdentity.ReadSerial(data);
            var hardware = TrueNasHardware.ParseInventory(data);
            var uptime = hardware.UptimeSeconds is >= 0 and <= 9007199254740991d ? hardware.UptimeSeconds : null;
            return new(version, serial, TrueNasConnectionSettings.SupportsRelease(version), hardware,
                new("truenas-24.10-system-context-v1", serial, data.TryGetProperty("version", out var reported) &&
                    reported.ValueKind == JsonValueKind.String ? reported.GetString() : null, default, started, finished, uptime));
        }

        public Task<SourceRead<DiskInventory[]>> ReadDisksAsync(CancellationToken ct) =>
            ReadArray("disk.query", StorageInventory.Parse, ct);

        public Task<SourceRead<ReportedPool[]>> ReadPoolsAsync(CancellationToken ct) =>
            ReadArray("pool.query", row => new ReportedPool(Text(row, "name"), Text(row, "guid"), Text(row, "status"),
                row.TryGetProperty("topology", out var topology) ? SanitizeTopology(topology) : null,
                PoolHistory.Parse(Guid.Empty, row)), ct);

        public Task<SourceRead<ReportedAlert[]>> ReadAlertsAsync(CancellationToken ct) =>
            ReadArray("alert.list", row => new ReportedAlert(Text(row, "id"), Text(row, "level"), Text(row, "klass"),
                Text(row, "formatted"), row.TryGetProperty("dismissed", out var dismissed) && dismissed.ValueKind == JsonValueKind.True), ct);

        public async Task<DatasetSnapshot> ReadDatasetsAsync(CancellationToken ct) =>
            Datasets.Normalize(await client.ReadAsync("pool.dataset.query", ct), DateTime.UtcNow);

        public async Task<CachedTemperatureSnapshot> ReadTemperaturesAsync(CancellationToken ct) =>
            CachedTemperatures.Normalize(await client.ReadAsync("disk.temperatures", ct), DateTime.UtcNow);

        public Task<ApiPowerOnSnapshot> ReadPowerOnHoursAsync(DiskInventory[] disks, Guid revision, string? previous, CancellationToken ct) =>
            ApiPowerOnHours.CollectAsync(client, disks, revision, ct, previous);

        public async Task<HardwareReporting> ReadHardwareAsync(bool backfill, CancellationToken ct)
        {
            var data = await client.ReadHardwareMetricsAsync(backfill, ct);
            // Use the effective persisted memory inventory and cycle timestamp when staging.
            return (server, revision, now, total) => TrueNasHardware.Normalize(data, server, revision, now, total);
        }

        public async Task<UpsSnapshot> ReadUpsAsync(CancellationToken ct) =>
            UpsSnapshot.Read(await client.ReadUpsMetricsAsync(ct), DateTime.UtcNow);

        public Task<UpsDevice[]?> ReadUpsDevicesAsync(NasConnection profile, CancellationToken ct) =>
            UpsNominalRating.ReadAsync(profile, ct);

        private async Task<SourceRead<T[]>> ReadArray<T>(string method, Func<JsonElement, T> map, CancellationToken ct)
        {
            try
            {
                var data = await client.ReadAsync(method, ct);
                return ValidArray(data, method)
                    ? new(data.EnumerateArray().Select(map).ToArray(), "supported")
                    : new(null, "unavailable");
            }
            catch (NasReadException error)
            {
                return new(null, error.Status);
            }
            // Transport and cancellation failures still discard the uncommitted cycle.
        }
    }

    private static bool ValidArray(JsonElement data, string method)
    {
        if (data.ValueKind != JsonValueKind.Array)
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                return false;
            if (row.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != row.EnumerateObject().Count())
                return false;
            var fields = method switch
            {
                "disk.query" => new[]
                {
                    "serial",
                    "model",
                    "name",
                    "wwn"
                },
                "pool.query" => ["name", "guid", "status"],
                _ => ["id", "level", "klass", "formatted"]
            };
            foreach (var field in fields)
                if (row.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return false;
            if (method == "disk.query")
            {
                if (!row.TryGetProperty("serial", out _) || !row.TryGetProperty("model", out _) || !row.TryGetProperty("name", out _))
                    return false;
            }
            else
            {
                var key = method == "pool.query" ? "guid" : "id";
                if (!row.TryGetProperty(key, out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) || !ids.Add(id.GetString()!))
                    return false;
            }

            if (method == "pool.query" && row.TryGetProperty("topology", out var topology))
            {
                if (topology.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                    return false;
                try
                {
                    _ = SanitizeTopology(topology);
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static object? SanitizeTopology(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Object => node.EnumerateObject().Where(p => new[] { "data", "special", "dedup", "log", "cache", "spare", "type", "guid", "name", "path", "status", "children", "stats", "read_errors", "write_errors", "checksum_errors" }.Contains(p.Name)).ToDictionary(p => p.Name, p => SanitizeTopology(p.Value)),
        JsonValueKind.Array => node.EnumerateArray().Select(SanitizeTopology).ToArray(),
        JsonValueKind.String => node.GetString(),
        JsonValueKind.Number => node.GetRawText(),
        _ => null
    };
    private static string Text(JsonElement row, string name) => row.TryGetProperty(name, out var value) ? value.ToString() : "";
}
