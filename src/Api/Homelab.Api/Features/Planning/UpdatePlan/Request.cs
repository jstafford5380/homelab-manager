using FastEndpoints;

namespace Homelab.Api.Features.Planning.UpdatePlan;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public PlanningItem Input { get; set; } = default !;
}
