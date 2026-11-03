
namespace Homelab.Api;
public record SpareReviewInput(Guid RequestId, Guid AssetId, DateTime AssetUpdatedUtc, string ConfirmedIdentity, string Verdict, string Evidence, DateTime ValidUntilUtc);
