
namespace Homelab.Api;
public record DrivePathInput(Guid RequestId, Guid AssetId, DateTime AssetUpdatedUtc, string ConfirmedIdentity, bool Attached, string Evidence, DateTime ValidUntilUtc, long? PreviousReviewId = null);
