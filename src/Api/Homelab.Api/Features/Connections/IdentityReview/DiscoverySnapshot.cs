
namespace Homelab.Api;
public record DiscoverySnapshot(Guid Revision, DateTime FetchedUtc, IdentityCandidate[] Rows);
