using FastEndpoints;

namespace Homelab.Api.Features.Storage.Observations.ImportObservation;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public Observation Input { get; set; } = default !;
}
