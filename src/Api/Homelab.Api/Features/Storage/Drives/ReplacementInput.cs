
namespace Homelab.Api;
public record ReplacementInput(Guid OldDiskId, Guid NewDiskId, DateTime OccurredUtc, string DateConfidence, string PoolGuid, string MembershipEvidence, string Reason, string Evidence, string ConfirmedOldIdentity, string ConfirmedNewIdentity, bool UseServerTime = false);
