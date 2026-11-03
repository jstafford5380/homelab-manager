namespace Homelab.Api.Features.Storage.Pools.GetDailyHistory;
public sealed record Response(IEnumerable<PoolDailySummary> Items, DateTime? NextDay, long? NextId, int DetailDays, int SummaryYears, bool BackupConfigured, int PendingSnapshots);
