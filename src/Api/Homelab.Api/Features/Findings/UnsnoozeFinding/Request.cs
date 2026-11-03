using FastEndpoints;

namespace Homelab.Api.Features.Findings.UnsnoozeFinding;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
