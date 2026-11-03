namespace Homelab.Api.Features.Storage.Drives.GetIdentityHistory;
public sealed record ResponseMembersItem(Guid Id, string Serial, string Model, string? Wwn, string MappingConfidence, bool Present, string? Bay, bool BayVerified, string Confirmation);
public sealed record ResponseCandidatesItem(Guid Id, string Serial, string Model, string? Wwn, string Confirmation);
public sealed record Response(string ReviewToken, IEnumerable<ResponseMembersItem> Members, IEnumerable<ResponseCandidatesItem> Candidates, System.Linq.IOrderedEnumerable<IdentityHistoryMerge> Links, IEnumerable<Observation> Observations, long? NextBefore, Finding[] Findings);
