using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetAcquisition;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
