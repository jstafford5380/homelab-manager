using FastEndpoints;

namespace Homelab.Api.Features.Findings.SnoozeFinding;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public SnoozeInput Input { get; set; } = default !;
}
