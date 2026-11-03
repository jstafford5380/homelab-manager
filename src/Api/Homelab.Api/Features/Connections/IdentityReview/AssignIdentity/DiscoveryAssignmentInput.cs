
namespace Homelab.Api;
public record DiscoveryAssignmentInput(Guid RequestId, string ReviewToken, string Wwn, string ConfirmedWwn, Guid? DiskId, string Evidence, DateTime ValidUntilUtc, bool ConfirmedPhysicalIdentity);
