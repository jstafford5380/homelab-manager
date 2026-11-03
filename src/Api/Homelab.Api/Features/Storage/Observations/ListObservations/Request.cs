using FastEndpoints;

namespace Homelab.Api.Features.Storage.Observations.ListObservations;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
