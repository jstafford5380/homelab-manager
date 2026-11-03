using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.RecordHardwarePath;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public DrivePathInput Input { get; set; } = default !;
}
