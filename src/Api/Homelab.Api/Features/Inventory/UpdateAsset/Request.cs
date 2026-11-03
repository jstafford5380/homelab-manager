using FastEndpoints;

namespace Homelab.Api.Features.Inventory.UpdateAsset;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public InventoryAsset Input { get; set; } = default !;
}
