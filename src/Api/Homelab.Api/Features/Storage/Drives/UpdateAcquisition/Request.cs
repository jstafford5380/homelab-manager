using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateAcquisition;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public DriveAcquisitionInput Input { get; set; } = default !;
}
