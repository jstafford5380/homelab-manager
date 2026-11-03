using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateLocation;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public StorageLocationInput Input { get; set; } = default !;
}
