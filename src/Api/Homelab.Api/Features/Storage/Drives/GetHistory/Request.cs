using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
