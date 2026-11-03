namespace Homelab.Api.Features.Storage.GetInventory;
public sealed record ResponseSystemsItem(Guid Id, string Name, string Status, DateTime? LastSuccessUtc, string? TopologyJson, RedundancyAssessment[] Redundancy);
public sealed record ResponseCapabilitiesItem(Guid ConnectionId, string Name, string Status, string Detail, DateTime? LastSuccessUtc);
public sealed record Response(IEnumerable<ResponseSystemsItem> Systems, IEnumerable<ResponseCapabilitiesItem> Capabilities, List<NativeAlert> NativeAlerts);
