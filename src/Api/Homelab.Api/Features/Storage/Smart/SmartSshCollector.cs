using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class SmartSshCollector(IServiceScopeFactory scopes, IConfiguration configuration, ISmartSshTransport transport, WriteCoordinator writes, ILogger<SmartSshCollector> logger) : BackgroundService
{
    private readonly Dictionary<Guid, DateTime> attempts = new();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (configuration.GetValue<bool>("Homelab:SmartEnabled"))
            {
                foreach (var server in configuration.GetSection("Homelab:SmartSsh:Servers").Get<SmartSshServer[]>() ?? [])
                {
                    if (!SmartSshTransport.ValidServer(server))
                    {
                        logger.LogWarning("Ignoring an invalid SMART SSH configuration.");
                        continue;
                    }

                    if (attempts.TryGetValue(server.ConnectionId, out var attempted) && DateTime.UtcNow - attempted < TimeSpan.FromHours(server.IntervalHours))
                        continue;
                    try
                    {
                        using var scope = scopes.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
                        var nas = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == server.ConnectionId, stoppingToken);
                        if (nas is null || !nas.Enabled)
                            continue;
                        attempts[server.ConnectionId] = DateTime.UtcNow;
                        var last = await db.SmartReads.Where(x => db.Disks.Any(d => d.Id == x.DiskId && d.ConnectionId == server.ConnectionId)).Select(x => (DateTime? )x.AttemptUtc).MaxAsync(stoppingToken);
                        if (last is not null && DateTime.UtcNow - last < TimeSpan.FromHours(server.IntervalHours))
                            continue;
                        var disks = await db.Disks.AsNoTracking().Where(x => x.ConnectionId == server.ConnectionId && x.Present && x.MappingConfidence != "ambiguous").ToListAsync(stoppingToken);
                        var scan = await transport.RunAsync(server, "smartctl --scan", stoppingToken);
                        if (scan.ExitCode != 0)
                            throw new InvalidDataException("SMART scan unavailable.");
                        var devices = scan.Output.Split('\n').Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()).OfType<string>().Where(SmartSshTransport.ValidDevice).Distinct().ToHashSet(StringComparer.Ordinal);
                        foreach (var disk in disks)
                        {
                            if (disks.Count(x => x.Alias == disk.Alias) != 1 || !devices.Contains("/dev/" + disk.Alias))
                                continue;
                            var result = await transport.RunAsync(server, "smartctl --json -x -n standby /dev/" + disk.Alias, stoppingToken);
                            using var lease = await writes.EnterAsync(stoppingToken);
                            await db.Entry(nas).ReloadAsync(stoppingToken);
                            var current = await db.Disks.SingleAsync(x => x.Id == disk.Id, stoppingToken);
                            if (!nas.Enabled || current.MappingConfidence == "ambiguous" || !current.Present || current.Alias != disk.Alias || StorageInventory.Identity(current) != StorageInventory.Identity(disk))
                                continue;
                            await StageAsync(db, current, result, server.ConfirmAtaCounterUnits, DateTime.UtcNow, stoppingToken);
                            await db.SaveChangesAsync(stoppingToken);
                        }
                    }
                    catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception e)when (e is not OutOfMemoryException)
                    {
                        logger.LogWarning("SMART collection could not complete for {ConnectionId}: {Failure}", server.ConnectionId, e.GetType().Name);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public static bool Sleeping(SmartCommandResult result)
    {
        // smartctl -n's low-power exit is only accepted with its explicit message.
        // Never retry without -n when a read fails or is skipped.
        try
        {
            using var json = JsonDocument.Parse(result.Output);
            if (!json.RootElement.TryGetProperty("smartctl", out var tool) || !tool.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                return false;
            return (result.ExitCode & 2) != 0 && messages.EnumerateArray().Any(m => m.TryGetProperty("string", out var s) && s.GetString()is { } text && (text.Contains("STANDBY", StringComparison.OrdinalIgnoreCase) || text.Contains("SLEEP", StringComparison.OrdinalIgnoreCase)) && text.Contains("exit", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static async Task StageAsync(HomelabDb db, Disk disk, SmartCommandResult result, bool confirmUnits, DateTime now, CancellationToken ct = default)
    {
        var read = await db.SmartReads.SingleOrDefaultAsync(x => x.DiskId == disk.Id, ct);
        if (read is null)
        {
            read = new()
            {
                DiskId = disk.Id
            };
            db.SmartReads.Add(read);
        }

        read.AttemptUtc = now;
        read.SkippedAsleep = Sleeping(result);
        read.Error = null;
        if (read.SkippedAsleep)
            return;
        try
        {
            if (result.ExitCode == 255)
                throw new InvalidDataException("SSH unavailable.");
            // Unknown reset continuity stays unknown; the adapter never invents an epoch.
            var preview = SmartReports.Parse(disk, new(result.Output, "", confirmUnits), now);
            if ((preview.ExitStatus & 7) != 0 || preview.ObservedUtc is null)
                throw new InvalidDataException("SMART read was incomplete or untimed.");
            read.ReportJson = JsonSerializer.Serialize(preview);
            read.ReadUtc = preview.ObservedUtc;
            foreach (var r in preview.Readings.Append(new("smart.overall_failed", preview.OverallSmart?.Passed is { } p ? p ? 0 : 1 : null, "flag", preview.OverallSmart?.Quality ?? "unknown", null)))
            {
                var observation = new Observation
                {
                    SourceKey = $"ssh-smart:{disk.Id:N}:{preview.ReportHash}:{r.Metric}",
                    Metric = r.Metric,
                    Value = r.Value,
                    Unit = r.Unit,
                    Quality = r.Quality,
                    MissingReason = r.MissingReason,
                    ObservedUtc = preview.ObservedUtc,
                    Source = "Restricted SSH smartctl read",
                    SourceSchema = preview.SourceSchema,
                    ResetEpoch = ""
                };
                if (ObservationValidation.Validate(observation, now)is null && !await db.Observations.AnyAsync(x => x.ConnectionId == disk.ConnectionId && x.SourceKey == observation.SourceKey, ct))
                    await ObservationIngestion.StageAsync(db, disk, observation, "SMART reader");
            }
        }
        catch (Exception e)when (e is JsonException or InvalidDataException)
        {
            read.Error = "The latest SMART read was unavailable. Showing the last successful reading, if any.";
        }
    }
}
