using FastEndpoints;

namespace Homelab.Api.Features.Inventory.AddDependency;
public sealed class Request
{
    [FromBody]
    public AssetDependency Input { get; set; } = default !;
}
