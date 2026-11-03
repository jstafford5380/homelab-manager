using FastEndpoints;

namespace Homelab.Api.Features.Connections.UpdateConnection;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public ConnectionSettings Input { get; set; } = default !;
}
