using FastEndpoints;

namespace Homelab.Api.Features.Findings.AcknowledgeFinding;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
