using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class DatasetHistory
{
    public static async Task StageAsync(HomelabDb db, NasConnection nas, DatasetSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot.FetchedUtc.Kind != DateTimeKind.Utc || snapshot.Items.Length > 256)
            throw new InvalidDataException("Invalid dataset history sample.");
        var hour = new DateTime(snapshot.FetchedUtc.Ticks - snapshot.FetchedUtc.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
        if (db.DatasetSamples.Local.Any(x => x.ConnectionId == nas.Id && x.ConfigurationRevision == nas.ConfigurationRevision && x.HourUtc == hour) || await db.DatasetSamples.AnyAsync(x => x.ConnectionId == nas.Id && x.ConfigurationRevision == nas.ConfigurationRevision && x.HourUtc == hour, ct))
            return;
        db.DatasetSamples.Add(new() { ConnectionId = nas.Id, ConfigurationRevision = nas.ConfigurationRevision, HourUtc = hour, FetchedUtc = snapshot.FetchedUtc, SystemSerial = nas.ObservedSystemSerial, Version = nas.Version, SnapshotJson = JsonSerializer.Serialize(snapshot) });
    }

    public static async Task<int> ExpireAsync(HomelabDb db, DateTime cutoff, CancellationToken ct = default)
    {
        var ids = await db.DatasetSamples.Where(x => x.FetchedUtc < cutoff && !db.DatasetRenames.Any(r => r.FromSampleId == x.Id || r.ToSampleId == x.Id)).OrderBy(x => x.Id).Take(PoolRetention.BatchSize).Select(x => x.Id).ToArrayAsync(ct);
        return await db.DatasetSamples.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }
}
