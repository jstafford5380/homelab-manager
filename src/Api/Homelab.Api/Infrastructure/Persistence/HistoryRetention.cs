using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class HistoryRetention
{
    // The same entry point is used by the worker and backup-failure integration tests.
    public static async Task<int> MaintainAsync(HomelabDb db, BackupService backups, DateTime now, CancellationToken ct = default)
    {
        await HardwareRetention.Summarize(db, now, ct);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new OperatingSettings();
        var cutoff = PoolRetention.DetailCutoff(now, settings.SampleRetentionDays);
        var summaryCutoff = PoolRetention.SummaryCutoff(now);
        if (backups.Destination is not null && (await db.HardwareReadings.AnyAsync(r => r.Source == "collected" && r.TakenUtc < now.AddDays(-366), ct) || await db.HardwareHourly.AnyAsync(h => h.HourUtc < now.AddDays(-732), ct) || await db.DatasetSamples.AnyAsync(x => x.FetchedUtc < cutoff && !db.DatasetRenames.Any(r => r.FromSampleId == x.Id || r.ToSampleId == x.Id), ct) || await db.PoolSnapshots.AnyAsync(x => x.FetchedUtc < cutoff, ct) || await db.PoolScans.AnyAsync(x => x.LastFetchedUtc < summaryCutoff, ct) || await db.PoolDailySummaries.AnyAsync(x => x.DayUtc < summaryCutoff, ct) || await db.CollectionRuns.AnyAsync(x => x.StartedUtc < cutoff, ct) || await db.CollectionDailySummaries.AnyAsync(x => x.DayUtc < summaryCutoff, ct)))
        {
            await backups.CreateAsync(db, ct);
            await backups.RetainAsync(db, settings, ct);
            var count = await PoolRetention.CompactAsync(db, now, settings.SampleRetentionDays, ct);
            count += await CollectionHistory.CompactAsync(db, now, settings.SampleRetentionDays, ct);
            count += await HardwareRetention.Expire(db, now, ct);
            count += await DatasetHistory.ExpireAsync(db, cutoff, ct);
            return count;
        }

        return 0;
    }
}
