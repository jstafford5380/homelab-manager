using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Readings.UpdateReading;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public Guid ReadingId { get; set; }

    [FromBody]
    public HardwareReadingInput Input { get; set; } = default !;
}
