namespace Homelab.Api.Features.Inventory.GetInventory;
public sealed record Response(List<InventoryAsset> Assets, List<AssetDependency> Dependencies);
