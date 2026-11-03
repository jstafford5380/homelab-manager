using FastEndpoints;

namespace Homelab.Api.Features.Connections.IdentityReview.RevokeAssignment;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public Guid AssignmentId { get; set; }

    [FromBody]
    public DiscoveryRevokeInput Input { get; set; } = default !;
}
