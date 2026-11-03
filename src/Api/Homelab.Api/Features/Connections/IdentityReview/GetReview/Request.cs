using FastEndpoints;

namespace Homelab.Api.Features.Connections.IdentityReview.GetReview;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
