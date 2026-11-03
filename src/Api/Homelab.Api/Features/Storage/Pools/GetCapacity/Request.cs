using FastEndpoints;

namespace Homelab.Api.Features.Storage.Pools.GetCapacity;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public string Guid { get; set; } = default !;

    [QueryParam]
    public int? Days { get; set; }
}
