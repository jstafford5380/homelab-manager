namespace Homelab.Api.Features.Connections.IdentityReview.GetReview;
public sealed record ResponseGroupsItemRecordsItem(Guid Id, string Serial, string Model, string MappingConfidence, string Lifecycle);
public sealed record ResponseGroupsItem(string? Wwn, string Serial, string Model, IEnumerable<string> Aliases, bool Eligible, IEnumerable<ResponseGroupsItemRecordsItem> Records);
public sealed record Response(bool Usable, DateTime? FetchedUtc, string? ReviewToken, IEnumerable<ResponseGroupsItem> Groups, IEnumerable<DiscoveryAssignment> Assignments, bool HistoryTruncated);
