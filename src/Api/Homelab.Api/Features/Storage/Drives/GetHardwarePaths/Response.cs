namespace Homelab.Api.Features.Storage.Drives.GetHardwarePaths;
public sealed record ResponseCurrentHeadsItem(Guid AssetId, long Id, bool Attached, string Evidence, DateTime RecordedUtc);
public sealed record ResponseItemsItem(DrivePathReview Record, string Status);
public sealed record ResponsePeersItemComponentsItem(Guid AssetId, string AssetName, string AssetType, DateTime ValidUntilUtc);
public sealed record ResponsePeersItemFindingsItem(Guid Id, string Title, string Evidence, DateTime? EvidenceObservedUtc, DateTime LastSeenUtc, string State);
public sealed record ResponsePeersItem(Guid DiskId, string Serial, string Model, IEnumerable<ResponsePeersItemComponentsItem> Components, IEnumerable<ResponsePeersItemFindingsItem> Findings);
public sealed record Response(DateTime GeneratedUtc, IEnumerable<ResponseCurrentHeadsItem> CurrentHeads, IEnumerable<ResponseItemsItem> Items, long? NextBefore, bool PeersTruncated, IEnumerable<ResponsePeersItem> Peers);
