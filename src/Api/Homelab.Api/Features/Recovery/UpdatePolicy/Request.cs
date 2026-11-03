using FastEndpoints;

namespace Homelab.Api.Features.Recovery.UpdatePolicy;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public RecoveryPolicy Input { get; set; } = default !;
}
