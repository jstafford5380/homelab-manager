using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.AddComponent;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public HardwareComponentInput Input { get; set; } = default !;
}
