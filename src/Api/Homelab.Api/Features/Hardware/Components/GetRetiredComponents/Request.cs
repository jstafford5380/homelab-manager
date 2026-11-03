using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.GetRetiredComponents;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
