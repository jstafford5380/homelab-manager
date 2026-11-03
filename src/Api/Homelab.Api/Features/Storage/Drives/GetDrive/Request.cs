using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetDrive;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
