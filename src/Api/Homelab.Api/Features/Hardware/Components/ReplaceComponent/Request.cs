using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.ReplaceComponent;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public Guid ComponentId { get; set; }

    [FromBody]
    public HardwareReplaceInput Input { get; set; } = default !;
}
