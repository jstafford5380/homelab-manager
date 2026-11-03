using FastEndpoints;

namespace Homelab.Api.Features.Connections.AcceptIdentity;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public AcceptNasIdentity Input { get; set; } = default !;
}
