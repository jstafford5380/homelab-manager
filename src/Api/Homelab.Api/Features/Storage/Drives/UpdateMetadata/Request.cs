using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateMetadata;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public DiskMetadata Input { get; set; } = default !;
}
