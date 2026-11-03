using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Readings.AddReading;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public HardwareReadingInput Input { get; set; } = default !;
}
