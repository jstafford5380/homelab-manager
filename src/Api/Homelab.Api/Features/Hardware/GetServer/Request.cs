using FastEndpoints;

namespace Homelab.Api.Features.Hardware.GetServer;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public string? Range { get; set; }
}
