using FastEndpoints;

namespace Homelab.Api.Features.Connections.RefreshNow;

public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
    public string? Target { get; set; }
}
