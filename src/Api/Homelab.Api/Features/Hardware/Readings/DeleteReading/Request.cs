using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Readings.DeleteReading;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public Guid ReadingId { get; set; }

    [QueryParam]
    public Guid Revision { get; set; }
}
