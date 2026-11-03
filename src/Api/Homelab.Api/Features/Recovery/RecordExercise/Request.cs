using FastEndpoints;

namespace Homelab.Api.Features.Recovery.RecordExercise;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public RestoreExercise Input { get; set; } = default !;
}
