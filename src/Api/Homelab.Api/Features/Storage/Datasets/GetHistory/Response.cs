namespace Homelab.Api.Features.Storage.Datasets.GetHistory;
public sealed record ResponseItemsItem(long Id, Guid ConfigurationRevision, DateTime FetchedUtc, string? SystemSerial, string? Version, DatasetSnapshot? Snapshot);
public sealed record Response(long SnapshotId, IEnumerable<ResponseItemsItem> Items, long? NextBefore, int RetentionDays, string Sampling);
