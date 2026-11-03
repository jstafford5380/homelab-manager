using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.GetCachedTemperatures;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
