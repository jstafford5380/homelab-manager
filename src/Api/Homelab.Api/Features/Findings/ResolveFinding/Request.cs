using FastEndpoints;

namespace Homelab.Api.Features.Findings.ResolveFinding;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public FindingResolutionInput Input { get; set; } = default !;
}
