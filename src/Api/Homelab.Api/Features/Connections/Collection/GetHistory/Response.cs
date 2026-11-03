namespace Homelab.Api.Features.Connections.Collection.GetHistory;
public sealed record ResponseItemsItem(long Id, DateTime StartedUtc, DateTime EndedUtc, string Result, double? DurationMs, CollectionCoverage Coverage);
public sealed record Response(IEnumerable<ResponseItemsItem> Items, long? NextId);
