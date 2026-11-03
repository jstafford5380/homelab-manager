
namespace Homelab.Api;
public record IdentityMergeInput(Guid OtherDiskId, Guid RequestId, string ReviewToken, string FirstIdentity, string SecondIdentity, string Evidence, bool ConfirmedSamePhysicalDrive);
