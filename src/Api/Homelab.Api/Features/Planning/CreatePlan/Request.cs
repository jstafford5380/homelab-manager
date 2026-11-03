using FastEndpoints;

namespace Homelab.Api.Features.Planning.CreatePlan;
public sealed class Request
{
    [FromBody]
    public PlanningItem Input { get; set; } = default !;
}
