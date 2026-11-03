using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.GetHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public Guid ComponentId { get; set; }
}
