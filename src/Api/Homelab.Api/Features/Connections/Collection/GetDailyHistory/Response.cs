namespace Homelab.Api.Features.Connections.Collection.GetDailyHistory;
public sealed record ResponseItemsItem(long Id, DateTime DayUtc, string Result, long AttemptCount, DateTime FirstStartedUtc, DateTime LastStartedUtc, long DurationKnownCount, double? DurationMinMs, double? DurationMaxMs, CollectionCoverage Coverage);
public sealed record Response(IEnumerable<ResponseItemsItem> Items, DateTime? NextDay, long? NextId, int DetailDays, int SummaryYears, bool BackupConfigured, int PendingAttempts);
