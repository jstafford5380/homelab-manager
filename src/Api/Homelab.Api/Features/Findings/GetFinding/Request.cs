using FastEndpoints;

namespace Homelab.Api.Features.Findings.GetFinding;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
