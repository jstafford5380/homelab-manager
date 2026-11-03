using FastEndpoints;

namespace Homelab.Api.Features.Connections.IdentityReview.AssignIdentity;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public DiscoveryAssignmentInput Input { get; set; } = default !;
}
