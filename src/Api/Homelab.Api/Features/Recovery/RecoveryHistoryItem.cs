
namespace Homelab.Api;
public record RecoveryHistoryItem(Guid Id, Guid PolicyRevision, DateTime OccurredUtc, DateTime RecordedUtc, string Author, string Outcome, DateTime ReviewByUtc, DateTime? RecoveryPointUtc, bool? DestinationConfirmed, string? Scope, string? Checks, int? DurationMinutes, string Evidence, string? FailureReason);
