using FastEndpoints;

namespace Homelab.Api.Features.Inventory.ExportInventory;
public sealed class Request
{
    [QueryParam]
    public bool? Redacted { get; set; }
}
