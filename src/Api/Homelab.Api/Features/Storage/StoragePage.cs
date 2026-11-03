using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class StoragePage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Freshness(NasConnection c, DateTime now) => !c.Enabled ? "Paused" : c.LastSuccessUtc is null ? "No collection yet" : c.LastSuccessUtc > now || now - c.LastSuccessUtc > TimeSpan.FromSeconds(Math.Max(300L, c.PollSeconds * 3L)) ? "Out of date" : "Reporting";
    internal static T? Read<T>(string? json)
    {
        try
        {
            return json is null ? default : JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private record SavedPool(string Guid, string Name, string Status);
    public static async Task<StoragePageData> ReadAsync(HomelabDb db, DateTime now, CancellationToken ct = default, bool smartEnabled = false)
    {
        var connections = await db.Connections.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var disks = await db.Disks.AsNoTracking().ToListAsync(ct);
        var findings = await db.Findings.AsNoTracking().Where(x => x.State != "resolved").ToListAsync(ct);
        var alerts = await db.NativeAlerts.AsNoTracking().Where(x => !x.DismissedOnNas && x.AbsentSinceUtc == null).ToListAsync(ct);
        var acquisitions = await db.DriveAcquisitions.AsNoTracking().ToDictionaryAsync(x => x.DiskId, ct);
        var smartReads = smartEnabled ? await db.SmartReads.AsNoTracking().ToDictionaryAsync(x => x.DiskId, ct) : new Dictionary<Guid, SmartRead>();
        var imported = smartEnabled ? await db.SmartReports.AsNoTracking().GroupBy(x => x.DiskId).Select(g => g.OrderByDescending(x => x.ImportedUtc).First()).ToDictionaryAsync(x => x.DiskId, ct) : new Dictionary<Guid, SmartReport>();
        var plans = await db.Plans.AsNoTracking().Where(x => x.Status != "complete").OrderBy(x => x.DueUtc).ToListAsync(ct);
        var apiPowerOnStates = await db.ApiPowerOnStates.AsNoTracking().ToDictionaryAsync(x => x.ConnectionId, x => x.SnapshotJson, ct);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new();
        var capabilities = await db.Capabilities.AsNoTracking().ToListAsync(ct);
        var snapshots = await db.PoolSnapshots.AsNoTracking().GroupBy(x => new { x.ConnectionId, x.PoolGuid }).Select(g => g.OrderByDescending(x => x.FetchedUtc).ThenByDescending(x => x.Id).First()).ToListAsync(ct);
        var outlooks = await CapacityOutlook.ReadAsync(db, now, ct);
        var servers = new List<StorageServer>();
        foreach (var c in connections)
        {
            var fresh = Freshness(c, now);
            var stale = fresh != "Reporting";
            var poolRows = Read<SavedPool[]>(c.TopologyJson) ?? [];
            var localDisks = disks.Where(x => x.ConnectionId == c.Id).ToArray();
            var localAlerts = alerts.Where(x => x.ConnectionId == c.Id).ToArray();
            var inventoryCurrent = capabilities.Any(x => x.ConnectionId == c.Id && x.Name == "inventory" && x.EffectiveStatus(now) == "supported");
            var poolsCurrent = capabilities.Any(x => x.ConnectionId == c.Id && x.Name == "pools" && x.EffectiveStatus(now) == "supported");
            var localSnapshots = snapshots.Where(x => x.ConnectionId == c.Id && poolRows.Any(p => p.Guid == x.PoolGuid)).ToArray();
            var mappings = new List<(PoolSnapshot Pool, VdevNodeEvidence Member, StorageDriveGroup? Group)>();
            foreach (var snapshot in localSnapshots)
            {
                try
                {
                    if (snapshot.VdevEvidenceJson is { } evidence)
                    {
                        var nodes = VdevEvidence.Read(evidence).Nodes;
                        var driveGroups = StorageDriveGroups.Map(nodes, c.TopologyJson, snapshot.PoolGuid);
                        mappings.AddRange(nodes.Where(n => n.Type == "DISK").Select(n => (snapshot, n, driveGroups.GetValueOrDefault(n.Guid))));
                    }
                }
                catch (Exception e)when (e is JsonException or InvalidDataException)
                {
                }
            }

            var temperatures = settings.ReadCachedTemperatures ? Read<CachedTemperatureSnapshot>(c.CachedTemperaturesJson) : null;
            var temperatureFresh = !stale && capabilities.Any(x => x.ConnectionId == c.Id && x.Name == "cached_temperatures" && x.EffectiveStatus(now) == "supported");
            var drives = localDisks.Select(d =>
            {
                var matches = mappings.Where(m => m.Member.DriveMapping?.DiskId == d.Id || m.Member.DriveMapping?.DiskId is null && m.Member.DiskAlias == d.Alias && d.Present && d.MappingConfidence != "ambiguous" && d.LastSeenUtc <= m.Pool.FetchedUtc && m.Pool.FetchedUtc - d.LastSeenUtc <= TimeSpan.FromMinutes(settings.InventoryMinutes * 3L) && localDisks.Count(x => x.Present && x.Alias == d.Alias) == 1).ToArray();
                var match = matches.Length == 1 ? matches[0] : default;
                var member = match.Member;
                var smartRead = smartReads.GetValueOrDefault(d.Id);
                var reports = new[]
                {
                    StorageSmart.Preview(smartRead?.ReportJson),
                    StorageSmart.Preview(imported.GetValueOrDefault(d.Id)?.CuratedJson)
                };
                var report = reports.OfType<SmartReportPreview>().OrderByDescending(x => x.ObservedUtc).FirstOrDefault();
                var smart = smartEnabled ? StorageSmart.Health(smartRead, report) : null;
                var fs = findings.Where(f => f.DiskId == d.Id).ToList();
                if (d.MappingConfidence == "ambiguous")
                    fs.Add(new() { Title = "Drive identity needs review", Severity = "review" });
                var alertReason = StorageAlertReasons.ForDrive(d, localDisks, localAlerts, inventoryCurrent, !stale && capabilities.Any(x => x.ConnectionId == c.Id && x.Name == "alerts" && x.EffectiveStatus(now) == "supported"));
                var status = StorageStatusRules.Drive(stale, d.Present, member?.State, member?.ReadErrors, member?.WriteErrors, member?.ChecksumErrors, fs, alertReason is not null, member is not null, smartEnabled, smart?.Passed, smart?.ReadUtc is { } smartAt && smartAt <= now && now - smartAt <= TimeSpan.FromHours(36) && (smart.Passed is not null || smart.PowerOnHours is not null || smart.PendingSectors is not null || smart.ReallocatedSectors is not null || smart.NvmeWearPercent is not null), alertReason);
                if (!stale && status.Label is "Healthy" or "Spare" && (!inventoryCurrent || member is not null && !poolsCurrent))
                    status = new("Unknown", "neutral", "Inventory or pool collection is out of date or unavailable. The current drive status is unknown.");
                if (member is null && d.Lifecycle != "spare" && mappings.Any(x => x.Member.DriveMapping?.DiskId is null) && status.Label == "Spare")
                    status = new("Unknown", "neutral", "Pool membership could not be matched to this drive's stable identity.");
                var temperature = temperatureFresh && inventoryCurrent && d.Present && d.MappingConfidence != "ambiguous" && localDisks.Count(x => x.Present && x.Alias == d.Alias) == 1 ? temperatures?.Readings.SingleOrDefault(x => x.DeviceName == d.Alias)?.Celsius : null;
                var temperatureNote = !settings.ReadCachedTemperatures ? "Collection disabled in Settings." : !temperatureFresh ? "No recent temperature cache read is available." : temperatures?.Readings.Length == 0 ? "TrueNAS returned an empty temperature cache." : temperature is null ? "No cached temperature could be matched to this drive." : null;
                return new StorageDrive(d, d.Nickname ?? StorageInventory.SuggestedName(d.Model), status, match.Pool?.PoolGuid, match.Pool?.Name, member?.State, member?.ReadErrors, member?.WriteErrors, member?.ChecksumErrors, StorageInventory.BayConfirmed(d), temperature, temperature is null ? null : temperatures?.FetchedUtc, acquisitions.GetValueOrDefault(d.Id), plans.FirstOrDefault(p => p.DiskId == d.Id), smart, match.Group, temperatureNote, Homelab.Api.ApiPowerOnHours.ForDrive(c, d, settings.ReadApiPowerOnHours, now, apiPowerOnStates.GetValueOrDefault(c.Id)));
            }).ToArray();
            var datasets = settings.ReadDatasets ? Read<DatasetSnapshot>(c.DatasetsJson) : null;
            var datasetStatus = !settings.ReadDatasets ? "disabled" : stale ? "stale" : capabilities.FirstOrDefault(x => x.ConnectionId == c.Id && x.Name == "datasets")?.EffectiveStatus(now) ?? "unavailable";
            var redundancy = PoolRedundancy.FromStored(c.TopologyJson, true);
            var pools = poolRows.Select(p =>
            {
                var snapshot = localSnapshots.SingleOrDefault(x => x.PoolGuid == p.Guid);
                var members = drives.Where(d => d.PoolGuid == p.Guid).ToArray();
                var groups = redundancy.FirstOrDefault(x => x.PoolGuid == p.Guid)?.Groups.Where(g => g.Role is "data" or "special" or "dedup").ToArray() ?? [];
                var layouts = groups.Select(x => x.Layout == "MIRROR" ? "Mirror" : x.Layout).Distinct().ToArray();
                var poolStatus = StorageStatusRules.Pool(stale, p.Status, members.Select(d => d.Status));
                if (!stale && !poolsCurrent)
                    poolStatus = new("Last known: " + poolStatus.Label.ToLowerInvariant(), "neutral", null);
                var unmapped = Math.Max(0, mappings.Count(x => x.Pool.PoolGuid == p.Guid) - members.Length);
                if (!stale && unmapped > 0 && poolStatus.Label == "Healthy")
                    poolStatus = new("Partly unknown", "neutral", null);
                return new StoragePool(p.Guid, p.Name, poolStatus, layouts.Length == 0 ? "Layout unknown" : string.Join(" + ", layouts), mappings.Count(x => x.Pool.PoolGuid == p.Guid), groups.Length > 0 && groups.All(x => x.ConfiguredTolerance is not null) ? groups.Min(x => x.ConfiguredTolerance) : null, snapshot?.AllocatedBytes, snapshot?.SizeBytes, snapshot, outlooks.SingleOrDefault(x => x.ConnectionId == c.Id && x.PoolGuid == p.Guid), Datasets.ForStoragePool(datasets, p.Name), datasetStatus, members, unmapped);
            }).ToArray();
            var attention = drives.Count(x => x.Status.Label is "Watch" or "Act now" or "Missing");
            servers.Add(new(c.Id, c.Name, Uri.TryCreate(c.ApiUri, UriKind.Absolute, out var uri) ? uri.Host : "Host unknown", fresh, c.LastSuccessUtc, pools.Length, drives.Length, drives.Length > 0 && drives.All(x => x.Disk.SizeBytes is not null) ? drives.Sum(x => x.Disk.SizeBytes) : null, stale || attention == 0 && (!inventoryCurrent || !poolsCurrent || pools.Any(p => p.UnmappedMembers > 0)) ? null : attention, localAlerts, pools, drives.Where(d => d.PoolGuid is null).ToArray()));
        }

        return new(smartEnabled, settings.DriveAgeReviewYears, servers.ToArray(), InvestmentTotal.Storage(disks, acquisitions.Values));
    }
}
