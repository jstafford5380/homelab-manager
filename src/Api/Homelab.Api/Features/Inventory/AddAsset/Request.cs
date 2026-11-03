using FastEndpoints;

namespace Homelab.Api.Features.Inventory.AddAsset;
public sealed class Request
{
    [FromBody]
    public InventoryAsset Input { get; set; } = default !;
}
