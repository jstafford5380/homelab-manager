using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.UpdateComponent;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public Guid ComponentId { get; set; }

    [FromBody]
    public HardwareComponentInput Input { get; set; } = default !;
}
