using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Synology.AddServer;
public sealed class Request
{
    [FromBody]
    public SynologyInput Input { get; set; } = default !;
}
