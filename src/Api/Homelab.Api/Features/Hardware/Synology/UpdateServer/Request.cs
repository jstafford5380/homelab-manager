using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Synology.UpdateServer;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public SynologyInput Input { get; set; } = default !;
}
