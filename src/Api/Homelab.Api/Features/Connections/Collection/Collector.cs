using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class Collector(IServiceScopeFactory scopes, IDataProtectionProvider protection, ILogger<Collector> logger, WriteCoordinator writes, INasReadClientFactory clients, IConfiguration? configuration = null) : BackgroundService
{
    public enum RefreshTarget { Storage, Hardware }
    public enum CollectionResult { Completed, Busy, Unavailable }
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> collectionGates = new();

    public async Task<IResult> RefreshNowAsync(Guid id, RefreshTarget target, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        var profile = await db.Connections.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (profile is null)
            return Results.NotFound();
        if (!profile.Enabled)
            return Results.BadRequest(new { error = "Resume this connection in Settings before refreshing." });

        var result = await CollectAsync(profile, db, ct, refresh: target);
        return result switch
        {
            CollectionResult.Completed => Results.NoContent(),
            CollectionResult.Busy => Results.Conflict(new { error = "This server is already collecting. Try again shortly." }),
            _ => Results.Json(new { error = "The NAS did not return all requested readings. Any readings that succeeded have been saved." }, statusCode: 502)
        };
    }

    private readonly SemaphoreSlim manualPowerOnRun = new(1, 1);
    public async Task<IResult> RunPowerOnHoursNowAsync(CancellationToken ct)
    {
        if (!await manualPowerOnRun.WaitAsync(0, ct))
            return Results.Conflict(new { error = "A power-on hours read is already running." });
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            if (!await db.Settings.AsNoTracking().AnyAsync(x => x.ReadApiPowerOnHours, ct))
                return Results.BadRequest(new { error = "Save the power-on hours option as enabled before running it." });
            var ids = await db.Connections.AsNoTracking().Where(x => x.Enabled).Select(x => x.Id).ToArrayAsync(ct);
            if (ids.Length == 0)
                return Results.BadRequest(new { error = "No enabled NAS connections are available." });
            var results = new System.Collections.Concurrent.ConcurrentBag<PowerOnHoursServerResult>();
            await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (id, token) =>
            {
                await using var worker = scopes.CreateAsyncScope();
                var database = worker.ServiceProvider.GetRequiredService<HomelabDb>();
                var profile = await database.Connections.SingleAsync(x => x.Id == id, token);
                var started = DateTime.UtcNow;
                await CollectAsync(profile, database, token, forcePowerOnHours: true);
                var state = await database.ApiPowerOnStates.AsNoTracking().SingleOrDefaultAsync(x => x.ConnectionId == id, token);
                var snapshot = ApiPowerOnHours.Read(state?.SnapshotJson);
                var fresh = snapshot is not null && snapshot.FetchedUtc >= started && snapshot.Revision == profile.ConfigurationRevision;
                results.Add(new PowerOnHoursServerResult(profile.Name, fresh, fresh ? snapshot!.Readings.Count(x => x.Status == "supported") : 0, fresh ? snapshot!.Readings.Count(x => x.Status == "unsupported") : 0, fresh ? snapshot!.Readings.Count(x => x.Status == "denied") : 0, fresh ? snapshot!.Readings.Count(x => x.Status is not ("supported" or "unsupported" or "denied")) : 0));
            });
            return Results.Ok(new PowerOnHoursRunResult(results.ToArray()));
        }
        finally
        {
            manualPowerOnRun.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await CollectDueAsync(stop);
            }
            catch (OperationCanceledException)when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Collection cycle failed ({ErrorType}).", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // The hosted loop awaits this whole batch before starting another: each NAS has at
    // most one collection in flight. Network waits do not monopolize the other slots.
    public async Task CollectDueAsync(CancellationToken stop)
    {
        Guid[] due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            var now = DateTime.UtcNow;
            var active = await db.Connections.AsNoTracking().Where(x => x.Enabled).ToDictionaryAsync(x => x.Id, x => x.ConfigurationRevision, stop);
            await clients.RetainAsync(active, stop);
            var eventCandidates = clients.EventRefreshCandidates();
            var eventCutoff = now.AddSeconds(-30);
            due = await db.Connections.AsNoTracking().Where(x => x.Enabled && (x.NextAttemptUtc == null || x.NextAttemptUtc <= now || eventCandidates.Contains(x.Id) && x.ConsecutiveFailures == 0 && (x.LastAttemptUtc == null || x.LastAttemptUtc <= eventCutoff))).OrderBy(x => x.NextAttemptUtc).ThenBy(x => x.Id).Select(x => x.Id).ToArrayAsync(stop);
        }

        await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stop }, async (id, ct) =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
                // A queued connection may have been paused, deleted or rescheduled.
                var profile = await db.Connections.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (profile is null || !profile.Enabled)
                    return;
                var eventDue = clients.EventRefreshCandidates().Contains(id) && profile.ConsecutiveFailures == 0 && (profile.LastAttemptUtc == null || profile.LastAttemptUtc <= DateTime.UtcNow.AddSeconds(-30));
                if (profile.NextAttemptUtc > DateTime.UtcNow && !eventDue)
                    return;
                await CollectAsync(profile, db, ct);
            }
            catch (OperationCanceledException)when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("NAS {NasId} collection worker failed ({ErrorType}).", id, ex.GetType().Name);
            }
        });
    }

    public async Task<CollectionResult> CollectAsync(NasConnection profile, HomelabDb db, CancellationToken stop,
        bool forcePowerOnHours = false, RefreshTarget? refresh = null)
    {
        var gate = collectionGates.GetOrAdd(profile.Id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, stop))
            return CollectionResult.Busy;
        try
        {
            return await CollectCoreAsync(profile, db, stop, forcePowerOnHours, refresh);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CollectionResult> CollectCoreAsync(NasConnection profile, HomelabDb db,
        CancellationToken stop, bool forcePowerOnHours, RefreshTarget? refresh)
    {
        if (!profile.Enabled)
            return CollectionResult.Unavailable;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var started = DateTime.UtcNow;
        var revision = profile.ConfigurationRevision;
        var inventorySeconds = Math.Max(900, profile.PollSeconds);
        string? failedRead = null;
        try
        {
            var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new OperatingSettings();
            inventorySeconds = Math.Max(settings.InventoryMinutes * 60, profile.PollSeconds);
            var inventory = await db.Capabilities.AsNoTracking().SingleOrDefaultAsync(x => x.ConnectionId == profile.Id && x.Name == "inventory", ct);
            // A new or edited connection must re-establish inventory. Otherwise only a successful
            // inventory read advances this clock; pool/alert polls cannot refresh disk evidence.
            var powerOnState = await db.ApiPowerOnStates.SingleOrDefaultAsync(x => x.ConnectionId == profile.Id, ct);
            var powerOnDue = settings.ReadApiPowerOnHours && (forcePowerOnHours || ApiPowerOnHours.Due(profile, powerOnState?.SnapshotJson, started));
            var inventoryDue = refresh == RefreshTarget.Storage || powerOnDue || profile.NextAttemptUtc is null || inventory?.Status != "supported" || inventory.LastSuccessUtc is null || started - inventory.LastSuccessUtc.Value >= TimeSpan.FromSeconds(inventorySeconds);
            await using var client = clients.Create();
            await client.ConnectAsync(profile, protection.CreateProtector("nas-credentials").Unprotect(profile.ProtectedApiKey), ct);
            inventoryDue |= client.RequiresInventoryReconciliation;
            if (profile.CertificateSha256 is null && client.ServerCertificateSha256 is { } fingerprint)
            {
                using var trustLease = await writes.EnterAsync(ct);
                await db.Entry(profile).ReloadAsync(ct);
                if (profile.ConfigurationRevision != revision || !profile.Enabled)
                    return CollectionResult.Unavailable;
                profile.CertificateSha256 = fingerprint;
                profile.ConfigurationRevision = Guid.NewGuid();
                db.Activity.Add(new() { Action = "NAS certificate identity enrolled", Detail = profile.Id + " SHA-256 " + fingerprint });
                await db.SaveChangesAsync(ct);
                revision = profile.ConfigurationRevision;
            }

            var systemReadStarted = DateTime.UtcNow;
            var system = await client.ReadAsync("system.info", ct);
            var systemReadFinished = DateTime.UtcNow;
            var reportedVersion = Text(system, "version");
            if (!NasIdentity.SupportsRelease(reportedVersion))
            {
                using var unsupportedLease = await writes.EnterAsync(stop);
                await db.Entry(profile).ReloadAsync(stop);
                if (profile.ConfigurationRevision != revision || !profile.Enabled)
                    return CollectionResult.Unavailable;
                profile.Version = reportedVersion;
                profile.ConfigurationRevision = Guid.NewGuid();
                profile.Status = "Unsupported release — collection paused";
                profile.Enabled = false;
                profile.LastAttemptUtc = started;
                await RecordCapabilities(db, profile, started, "unsupported", "No adapter validated for this release.", null, inventorySeconds, true, stop);
                await db.SaveChangesAsync(stop);
                return CollectionResult.Unavailable;
            }

            using (var identityLease = await writes.EnterAsync(ct))
            {
                await db.Entry(profile).ReloadAsync(ct);
                if (profile.ConfigurationRevision != revision || !profile.Enabled)
                    return CollectionResult.Unavailable;
                profile.Version = reportedVersion;
                var blocked = await NasIdentity.CheckAsync(db, profile, NasIdentity.ReadSerial(system), DateTime.UtcNow, ct);
                if (blocked)
                {
                    profile.LastAttemptUtc = started;
                    await RecordCapabilities(db, profile, started, "unavailable", "System identity review required; no storage snapshot committed.", null, inventorySeconds, true, ct);
                }

                await db.SaveChangesAsync(ct);
                if (blocked)
                    return CollectionResult.Unavailable;
            }

            var eventStatuses = await client.SubscribeEventsAsync(ct);
            if (inventoryDue)
                failedRead = "inventory";
            var inventoryRead = inventoryDue ? await ReadArray(client, "disk.query", ct) : new SourceRead(null, "skipped");
            var disks = inventoryRead.Data;
            failedRead = "pools";
            var poolRead = await ReadArray(client, "pool.query", ct);
            failedRead = "alerts";
            var alertRead = await ReadArray(client, "alert.list", ct);
            var alerts = alertRead.Data;
            CachedTemperatureSnapshot? temperatures = null;
            var temperatureStatus = settings.ReadCachedTemperatures ? "unavailable" : "disabled";
            if (settings.ReadCachedTemperatures)
            {
                failedRead = "cached_temperatures";
                try
                {
                    temperatures = CachedTemperatures.Normalize(await client.ReadAsync("disk.temperatures", ct), DateTime.UtcNow);
                    temperatureStatus = "supported";
                }
                catch (NasReadException error)
                {
                    temperatureStatus = error.Status;
                }
                catch (InvalidDataException)
                {
                    temperatureStatus = "unavailable";
                }
            }

            DatasetSnapshot? datasetSnapshot = null;
            var datasetStatus = settings.ReadDatasets ? "unavailable" : "disabled";
            if (settings.ReadDatasets)
            {
                failedRead = "datasets";
                try
                {
                    datasetSnapshot = Datasets.Normalize(await client.ReadAsync("pool.dataset.query", ct), DateTime.UtcNow);
                    datasetStatus = "supported";
                }
                catch (NasReadException error)
                {
                    datasetStatus = error.Status;
                }
                catch (InvalidDataException)
                {
                    datasetStatus = "unavailable";
                }
            }

            JsonElement? hardwareMetrics = null;
            var hardwareState = await db.HardwareServers.AsNoTracking().SingleOrDefaultAsync(s => s.ConnectionId == profile.Id, ct);
            var hardwareDue = configuration?.GetValue<bool>("Homelab:HardwareMetricsEnabled") == true && (refresh == RefreshTarget.Hardware || hardwareState?.NextAttemptUtc is null || hardwareState.ConfigurationRevision != revision || hardwareState.NextAttemptUtc <= started);
            if (hardwareDue)
            {
                try
                {
                    hardwareMetrics = await client.ReadHardwareMetricsAsync(hardwareState?.CollectionStartedUtc is null || hardwareState.ConfigurationRevision != revision, ct);
                }
                catch (NasReadException)
                {
                }
                catch (InvalidDataException)
                {
                }
            }

            ApiPowerOnSnapshot? powerOnSnapshot = null;
            if (powerOnDue && disks is { } currentDisks)
            {
                try
                {
                    powerOnSnapshot = await ApiPowerOnHours.CollectAsync(client, currentDisks, revision, ct, powerOnState?.SnapshotJson);
                }
                catch (NasReadException)
                {
                }
                catch (InvalidDataException)
                {
                }
            }

            // Complete network reads before acquiring the writer, then commit one coherent snapshot.
            using var lease = await writes.EnterAsync(ct);
            await db.Entry(profile).ReloadAsync(ct);
            if (profile.ConfigurationRevision != revision || !profile.Enabled)
                return CollectionResult.Unavailable;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            profile.CertificateSha256 ??= client.ServerCertificateSha256;
            profile.Version = Text(system, "version");
            var hardwareServer = await HardwareCollection.StageInventory(db, profile, system, started, ct);
            if (hardwareDue)
                hardwareServer.NextAttemptUtc = started.AddMinutes(5);
            var hardwareSampled = false;
            if (hardwareMetrics is { } hardwareData)
            {
                try
                {
                    hardwareSampled = await HardwareCollection.StageMetrics(db, profile.Id, hardwareData, started, ct);
                }
                catch (InvalidDataException)
                { /* Invalid optional reporting does not discard successful inventory/storage reads. */
                }
            }

            IdentityCandidate[]? mappingCandidates = null;
            if (disks is { } diskSnapshot)
            {
                var candidates = diskSnapshot.EnumerateArray().Select(row => new IdentityCandidate(Text(row, "serial"), Text(row, "model"), Text(row, "wwn"), Text(row, "name"))).ToArray();
                await DiskIdentity.ReconcileAsync(db, profile.Id, candidates, DateTime.UtcNow, ct, revision);
                StorageInventory.Attach(db.Disks.Local.Where(x => x.ConnectionId == profile.Id).ToArray(), diskSnapshot);
                profile.DiscoveryIdentityJson = DiscoveryAssignments.Capture(revision, DateTime.UtcNow, candidates);
                mappingCandidates = candidates;
            }

            if (poolRead.Data is { } pools)
            {
                // Store a curated topology projection, never the complete server response.
                var topology = pools.EnumerateArray().Select(p => new { name = Text(p, "name"), guid = Text(p, "guid"), status = Text(p, "status"), topology = p.TryGetProperty("topology", out var t) ? SanitizeTopology(t) : null }).ToArray();
                profile.TopologyJson = JsonSerializer.Serialize(topology);
                foreach (var pool in pools.EnumerateArray())
                {
                    if (PoolHistory.Normalize(profile.Id, pool, DateTime.UtcNow)is { } snapshot)
                    {
                        snapshot.VdevEvidenceJson = VdevMapping.Attach(snapshot.VdevEvidenceJson!, mappingCandidates, db.Disks.Local.Where(x => x.ConnectionId == profile.Id).ToArray());
                        snapshot.SourceContextJson = VdevChanges.CaptureContext(system, revision, systemReadStarted, systemReadFinished);
                        await VdevFindings.RecordAsync(db, snapshot, ct);
                        await ResilverFindings.RecordAsync(db, snapshot, ct);
                        db.PoolSnapshots.Add(snapshot);
                        await PoolIntegrity.RecordAsync(db, snapshot, ct);
                        await PoolScanHistory.RecordAsync(db, snapshot, ct);
                    }
                }

                foreach (var pool in topology)
                {
                    var existingFinding = await db.Findings.FirstOrDefaultAsync(f => f.ConnectionId == profile.Id && f.Rule == "pool-state-v1:" + pool.guid && f.State != "resolved", ct);
                    if (existingFinding is not null)
                    {
                        existingFinding.HealthySamples = pool.status == "ONLINE" ? existingFinding.HealthySamples + 1 : 0;
                        if (existingFinding.HealthySamples >= 2)
                        {
                            FindingLifecycle.Resolve(existingFinding, DateTime.UtcNow, "Two successful consecutive pool snapshots reported ONLINE. Historical errors remain evidence; this does not establish recoverability.");
                            db.Activity.Add(new() { Action = "Pool finding resolved by current state", Detail = existingFinding.Id.ToString() });
                        }
                    }

                    if (pool.status is "DEGRADED" or "FAULTED" or "UNAVAIL" or "SUSPENDED" or "OFFLINE")
                        await UpsertFinding(db, profile.Id, "pool-state-v1:" + pool.guid, "Pool " + pool.name + " is " + pool.status, "TrueNAS reported this pool state at " + DateTime.UtcNow.ToString("O"), "Review native details, backup availability, and your incident runbook.", "critical", ct);
                }
            }

            if (alerts is { } native)
                await ReconcileAlerts(db, profile.Id, native, ct);
            // Cache entries have no observation timestamp or stable drive identity. Keep a bounded
            // latest snapshot by reported name; never attach these values to physical disk history.
            // Recheck the opt-in before committing; this snapshot never becomes a health finding.
            if (powerOnSnapshot is not null && await db.Settings.AsNoTracking().AnyAsync(x => x.ReadApiPowerOnHours, ct))
            {
                if (powerOnState is null)
                {
                    powerOnState = new()
                    {
                        ConnectionId = profile.Id
                    };
                    db.ApiPowerOnStates.Add(powerOnState);
                }

                powerOnState.SnapshotJson = JsonSerializer.Serialize(powerOnSnapshot);
            }

            if (temperatures is not null)
                profile.CachedTemperaturesJson = JsonSerializer.Serialize(temperatures);
            await RecordTemperatureCapability(db, profile, started, temperatureStatus, ct);
            if (datasetSnapshot is not null)
            {
                profile.DatasetsJson = JsonSerializer.Serialize(datasetSnapshot);
                await DatasetHistory.StageAsync(db, profile, datasetSnapshot, ct);
            }

            await Datasets.RecordCapabilityAsync(db, profile, started, datasetStatus, ct);
            await RecordEventCapabilities(db, profile, started, eventStatuses, ct);
            var sourceStatuses = new Dictionary<string, string>
            {
                ["inventory"] = inventoryRead.Status,
                ["pools"] = poolRead.Status,
                ["alerts"] = alertRead.Status
            };
            var anyFresh = sourceStatuses.Values.Contains("supported") || temperatureStatus == "supported" || datasetStatus == "supported";
            profile.LastAttemptUtc = started;
            profile.ConsecutiveFailures = anyFresh ? 0 : profile.ConsecutiveFailures + 1;
            profile.NextAttemptUtc = DateTime.UtcNow.AddSeconds(anyFresh ? profile.PollSeconds : BackoffSeconds(profile));
            await RecordCapabilities(db, profile, started, anyFresh ? "supported" : "unavailable", "Successful source reads committed independently; failed sources retain previous evidence.", alertRead.Status, inventorySeconds, inventoryDue, ct, sourceStatuses: sourceStatuses);
            if (anyFresh)
                profile.LastSuccessUtc = DateTime.UtcNow;
            profile.Status = !anyFresh ? "No source readings available — check capability coverage" : sourceStatuses.Values.Any(x => x is not ("supported" or "skipped")) || temperatureStatus is "denied" or "unavailable" or "unsupported" || datasetStatus is "denied" or "unavailable" or "unsupported" ? "Partial — check capability coverage" : "Partial — SMART and recovery unverified";
            await db.SaveChangesAsync(stop);
            await HardwareCollection.RecordStatuses(db, profile.Id, DateTime.UtcNow, configuration?.GetValue<bool>("Homelab:HardwareMetricsEnabled") == true, stop);
            await db.SaveChangesAsync(stop);
            await transaction.CommitAsync(stop);
            var completed = refresh switch
            {
                RefreshTarget.Storage => inventoryRead.Status == "supported" && poolRead.Status == "supported",
                RefreshTarget.Hardware => !hardwareDue || hardwareSampled,
                _ => anyFresh
            };
            return completed ? CollectionResult.Completed : CollectionResult.Unavailable;
        }
        catch (Exception ex)when (!stop.IsCancellationRequested)
        {
            // Discard all attempted normalization after a failure. Persist only collection status.
            var id = profile.Id;
            db.ChangeTracker.Clear();
            using var failureLease = await writes.EnterAsync(stop);
            profile = await db.Connections.SingleAsync(x => x.Id == id, stop);
            if (profile.ConfigurationRevision != revision || !profile.Enabled)
                return CollectionResult.Unavailable;
            profile.Status = ex is UnauthorizedAccessException ? "Authentication denied" : "Collection unavailable — check connection, trust, and permissions";
            profile.LastAttemptUtc = started;
            profile.ConsecutiveFailures++;
            profile.NextAttemptUtc = DateTime.UtcNow.AddSeconds(BackoffSeconds(profile));
            var status = ex is UnauthorizedAccessException ? "denied" : ex is NasReadException read ? read.Status : "unavailable";
            await RecordCapabilities(db, profile, started, status, "Snapshot not committed; previous evidence retained.", null, inventorySeconds, true, stop, failedRead);
            logger.LogWarning("NAS {NasId} collection failed ({ErrorType}).", profile.Id, ex.GetType().Name);
            await db.SaveChangesAsync(stop);
            return CollectionResult.Unavailable;
        }
    }

    private static async Task RecordCapabilities(HomelabDb db, NasConnection nas, DateTime started, string status, string detail, string? alerts, int inventorySeconds, bool inventoryCollected, CancellationToken ct, string? failedRead = null, IReadOnlyDictionary<string, string>? sourceStatuses = null)
    {
        var capabilities = await db.Capabilities.Where(x => x.ConnectionId == nas.Id).ToListAsync(ct);
        foreach (var name in new[]
        {
            "inventory",
            "pools",
            "alerts",
            "smart",
            "recovery"
        }

        )
        {
            var capability = capabilities.SingleOrDefault(x => x.Name == name);
            if (capability is null)
            {
                capability = new Capability
                {
                    ConnectionId = nas.Id,
                    Name = name
                };
                db.Capabilities.Add(capability);
                capabilities.Add(capability);
            }

            capability.ExpectedSeconds = name == "inventory" ? inventorySeconds : nas.PollSeconds;
            if (name == "inventory" && !inventoryCollected)
                continue;
            capability.LastAttemptUtc = started;
            capability.Status = name is "smart" or "recovery" ? "unsupported" : name == "alerts" ? alerts ?? (status == "supported" ? "unavailable" : status) : status;
            if (sourceStatuses is not null && sourceStatuses.TryGetValue(name, out var sourceStatus))
                capability.Status = sourceStatus;
            // A method-level denial does not prove that unattempted methods lack permission.
            if (sourceStatuses is null && status != "supported" && failedRead is not null && name != failedRead && name is not ("smart" or "recovery"))
                capability.Status = "unavailable";
            capability.Detail = name is "smart" or "recovery" ? "No validated automatic adapter for this capability. Manual evidence is available." : detail;
            if (sourceStatuses is not null && capability.Status is ("denied" or "unavailable" or "unsupported") && name is not ("smart" or "recovery"))
                capability.Detail = $"This source read was {capability.Status}; previous evidence and its last successful timestamp are retained.";
            if (capability.Status == "supported")
                capability.LastSuccessUtc = DateTime.UtcNow;
        }

        if (sourceStatuses is null && status != "supported")
        {
            await RecordEventCapabilities(db, nas, started, null, ct);
            var datasetsEnabled = (await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct))?.ReadDatasets == true;
            await Datasets.RecordCapabilityAsync(db, nas, started, !datasetsEnabled ? "disabled" : failedRead == "datasets" ? status : "unavailable", ct);
            var temperatureEnabled = (await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct))?.ReadCachedTemperatures == true;
            await RecordTemperatureCapability(db, nas, started, !temperatureEnabled ? "disabled" : failedRead == "cached_temperatures" ? status : "unavailable", ct);
        }

        db.CollectionRuns.Add(new() { ConnectionId = nas.Id, StartedUtc = started, EndedUtc = DateTime.UtcNow, Result = status == "supported" ? "partial" : status, CapabilitySummaryJson = JsonSerializer.Serialize(capabilities.Concat(db.Capabilities.Local.Where(x => x.ConnectionId == nas.Id)).DistinctBy(x => x.Name).Select(c => new { c.Name, c.Status, c.LastSuccessUtc, c.ExpectedSeconds })) });
    }

    private static async Task RecordEventCapabilities(HomelabDb db, NasConnection nas, DateTime started, IReadOnlyDictionary<string, string>? statuses, CancellationToken ct)
    {
        foreach (var collection in TrueNasClient.EventCollections)
        {
            var name = "events:" + collection;
            var capability = db.Capabilities.Local.SingleOrDefault(x => x.ConnectionId == nas.Id && x.Name == name) ?? await db.Capabilities.SingleOrDefaultAsync(x => x.ConnectionId == nas.Id && x.Name == name, ct);
            if (capability is null)
            {
                capability = new()
                {
                    ConnectionId = nas.Id,
                    Name = name
                };
                db.Capabilities.Add(capability);
            }

            capability.LastAttemptUtc = started;
            capability.ExpectedSeconds = nas.PollSeconds;
            capability.Status = statuses?.GetValueOrDefault(collection) ?? (statuses is null ? "unavailable" : "unsupported");
            capability.Detail = capability.Status == "supported" ? "Passive broadcast subscription confirmed on this session. Events request bounded snapshot reads; event payloads are not health evidence." : "Passive subscription is " + capability.Status + ". Periodic snapshot reads remain independent; no event completeness is assumed.";
            if (capability.Status == "supported")
                capability.LastSuccessUtc = DateTime.UtcNow;
        }
    }

    private static double BackoffSeconds(NasConnection profile) => Math.Min(Math.Max(900, profile.PollSeconds), profile.PollSeconds * Math.Pow(2, Math.Min(4, profile.ConsecutiveFailures))) + Random.Shared.Next(0, 10);
    private sealed record SourceRead(JsonElement? Data, string Status);
    private static async Task<SourceRead> ReadArray(INasReadClient client, string method, CancellationToken ct)
    {
        try
        {
            var data = await client.ReadAsync(method, ct);
            return ValidArray(data, method) ? new(data, "supported") : new(null, "unavailable");
        }
        catch (NasReadException error)
        {
            return new(null, error.Status);
        }
    // Broken transport, cancellation and unexpected implementation failures still discard the
    // uncommitted cycle. A lost session is not proof of per-method permission denial.
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

    private static async Task RecordTemperatureCapability(HomelabDb db, NasConnection nas, DateTime started, string status, CancellationToken ct)
    {
        var capability = db.Capabilities.Local.SingleOrDefault(x => x.ConnectionId == nas.Id && x.Name == "cached_temperatures") ?? await db.Capabilities.SingleOrDefaultAsync(x => x.ConnectionId == nas.Id && x.Name == "cached_temperatures", ct);
        if (capability is null)
        {
            capability = new()
            {
                ConnectionId = nas.Id,
                Name = "cached_temperatures"
            };
            db.Capabilities.Add(capability);
        }

        capability.Status = status;
        capability.ExpectedSeconds = nas.PollSeconds;
        capability.LastAttemptUtc = status == "disabled" ? capability.LastAttemptUtc : started;
        if (status == "supported")
            capability.LastSuccessUtc = DateTime.UtcNow;
        capability.Detail = status == "disabled" ? "Cache-only temperatures are off in Settings." : "Cache-only API read. Values have no observation timestamp or durable disk identity; missing entries are not zero. Full SMART coverage remains unsupported.";
    }

    private static async Task ReconcileAlerts(HomelabDb db, Guid nas, JsonElement snapshot, CancellationToken ct)
    {
        var existing = await db.NativeAlerts.Where(x => x.ConnectionId == nas).ToListAsync(ct);
        var seen = new HashSet<string>();
        var now = DateTime.UtcNow;
        foreach (var row in snapshot.EnumerateArray())
        {
            var id = Text(row, "id");
            if (string.IsNullOrEmpty(id))
                continue;
            seen.Add(id);
            var alert = existing.SingleOrDefault(x => x.SourceId == id);
            if (alert is null)
            {
                alert = new NativeAlert
                {
                    ConnectionId = nas,
                    SourceId = id
                };
                db.NativeAlerts.Add(alert);
                existing.Add(alert);
            }

            alert.Level = Text(row, "level");
            alert.Class = Text(row, "klass");
            alert.Message = Text(row, "formatted");
            alert.LastSeenUtc = now;
            alert.AbsentSinceUtc = null;
            alert.DismissedOnNas = row.TryGetProperty("dismissed", out var dismissed) && dismissed.ValueKind == JsonValueKind.True;
        }

        foreach (var alert in existing.Where(x => !seen.Contains(x.SourceId) && x.AbsentSinceUtc == null))
            alert.AbsentSinceUtc = now;
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
    private static async Task UpsertFinding(HomelabDb db, Guid id, string rule, string title, string evidence, string nextStep, string severity, CancellationToken ct)
    {
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.ConnectionId == id && x.Rule == rule && x.State != "resolved", ct);
        if (finding is null)
        {
            finding = new Finding
            {
                ConnectionId = id,
                Rule = rule,
                Title = title,
                Evidence = evidence,
                NextStep = nextStep,
                Severity = severity
            };
            db.Findings.Add(finding);
        }

        finding.LastSeenUtc = DateTime.UtcNow;
    }
}
