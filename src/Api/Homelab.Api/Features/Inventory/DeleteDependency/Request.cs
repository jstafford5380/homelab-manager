using FastEndpoints;

namespace Homelab.Api.Features.Inventory.DeleteDependency;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
