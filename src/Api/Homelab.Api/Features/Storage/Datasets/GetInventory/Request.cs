using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.GetInventory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
