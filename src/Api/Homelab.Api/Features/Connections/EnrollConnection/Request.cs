using FastEndpoints;

namespace Homelab.Api.Features.Connections.EnrollConnection;
public sealed class Request
{
    [FromBody]
    public ConnectionInput Input { get; set; } = default !;
}
