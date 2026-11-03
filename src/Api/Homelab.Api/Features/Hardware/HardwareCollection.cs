using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class HardwareCollection
{
    public static Task<HardwareServer> StageInventory(HomelabDb db, NasConnection connection, JsonElement system, DateTime now, CancellationToken ct) =>
        StageInventory(db, connection, TrueNasHardware.ParseInventory(system), now, ct);

    internal static async Task<HardwareServer> StageInventory(HomelabDb db, NasConnection connection, HardwareInventorySample sample, DateTime now, CancellationToken ct)
    {
        var s = await db.HardwareServers.SingleOrDefaultAsync(s => s.ConnectionId == connection.Id, ct);
        if (s is null)
        {
            s = new()
            {
                Id = connection.Id,
                ConnectionId = connection.Id
            };
            db.HardwareServers.Add(s);
        }

        if (s.ConfigurationRevision != connection.ConfigurationRevision)
        {
            s.CollectionStartedUtc = null;
            s.LastMetricsUtc = null;
            s.UpsSnapshotJson = null;
        }

        s.ConfigurationRevision = connection.ConfigurationRevision;
        s.Name = connection.Name;
        s.Version = sample.Version;
        s.UptimeSeconds = sample.UptimeSeconds;
        s.LastSuccessUtc = now;
        s.Connected = true;
        await StageComponents(db, s.Id, sample, now, ct);
        return s;
    }

    internal static async Task StageComponents(HomelabDb db, Guid serverId, HardwareInventorySample sample, DateTime now, CancellationToken ct)
    {
        foreach (var component in sample.Components)
            await Detected(db, serverId, component.Kind, component.Name, component.Spec, component.TotalBytes, component.Failed, now, ct);
    }

    public static async Task Detected(HomelabDb db, Guid serverId, string kind, string name, string spec, double? total, bool? failed, DateTime now, CancellationToken ct)
    {
        var key = kind;
        var c = await db.HardwareComponents.SingleOrDefaultAsync(c => c.ServerId == serverId && c.DetectedKey == key && c.RetiredUtc == null, ct);
        if (c is null)
        {
            c = new()
            {
                ServerId = serverId,
                Kind = kind,
                DetectedKey = key
            };
            db.HardwareComponents.Add(c);
            db.HardwareEvents.Add(new() { ComponentId = c.Id, AtUtc = now, Kind = "detected", Text = "Detected " + name + " · " + spec });
        }
        else if (c.DetectedName != name || c.DetectedSpec != spec || c.DetectedTotalBytes != total || c.FanFailed != failed)
        {
            db.HardwareEvents.Add(new() { ComponentId = c.Id, AtUtc = now, Kind = "detected-change", Text = $"Detected values changed: {c.DetectedName} / {c.DetectedSpec} / {c.DetectedTotalBytes} bytes → {name} / {spec} / {total} bytes." });
            c.Revision = Guid.NewGuid();
        }

        c.DetectedName = name;
        c.DetectedSpec = spec;
        c.DetectedTotalBytes = total;
        c.FanFailed = failed;
    }

    public static HardwareReading[] Normalize(JsonElement data, Guid serverId, Guid revision, DateTime now, double? total) =>
        TrueNasHardware.Normalize(data, serverId, revision, now, total);

    internal static async Task<bool> StageMetrics(HomelabDb db, Guid serverId, HardwareReporting reporting, DateTime now, CancellationToken ct)
    {
        var s = db.HardwareServers.Local.SingleOrDefault(s => s.Id == serverId) ?? await db.HardwareServers.SingleAsync(s => s.Id == serverId, ct);
        var memory = db.HardwareComponents.Local.FirstOrDefault(c => c.ServerId == serverId && c.Kind == "memory") ?? await db.HardwareComponents.FirstOrDefaultAsync(c => c.ServerId == serverId && c.Kind == "memory" && c.RetiredUtc == null, ct);
        var rows = reporting(serverId, s.ConfigurationRevision, now, memory?.DetectedTotalBytes);
        var existing = await db.HardwareReadings.Where(r => r.ServerId == serverId && r.Source == "collected" && r.ConfigurationRevision == s.ConfigurationRevision && r.TakenUtc >= now.AddDays(-1)).Select(r => r.TakenUtc).ToArrayAsync(ct);
        var added = rows.Where(r => !existing.Contains(r.TakenUtc)).ToArray();
        db.HardwareReadings.AddRange(added);
        if (rows.Length > 0)
        {
            s.CollectionStartedUtc ??= rows.Min(r => r.TakenUtc);
            s.LastMetricsUtc = rows.Max(r => r.TakenUtc);
        }

        return rows.Length > 0;
    }

    public static async Task RecordStatuses(HomelabDb db, Guid serverId, DateTime now, bool metricsEnabled, CancellationToken ct)
    {
        var s = await db.HardwareServers.SingleAsync(s => s.Id == serverId, ct);
        var detail = await HardwareMetrics.Detail(db, s, now, 30, metricsEnabled, ct);
        if (detail.Server.Freshness != "Reporting")
            return;
        foreach (var view in detail.Components)
        {
            var c = await db.HardwareComponents.SingleAsync(c => c.Id == view.Record.Id, ct);
            if (c.LastStatus != view.Status.Label)
            {
                db.HardwareEvents.Add(new() { ComponentId = c.Id, AtUtc = now, Kind = "status", Text = view.Status.Label + ": " + view.Status.Why });
            }

            c.LastStatus = view.Status.Label;
            c.LastWhy = view.Status.Why;
        }
    }
}

internal sealed record HardwareInventorySample(string? Version, double? UptimeSeconds, IReadOnlyList<DetectedHardware> Components);
internal sealed record DetectedHardware(string Kind, string Name, string Spec, double? TotalBytes = null, bool? Failed = null);
