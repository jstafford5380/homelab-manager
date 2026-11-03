using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateProfile;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public StorageProfileInput Input { get; set; } = default !;
}
