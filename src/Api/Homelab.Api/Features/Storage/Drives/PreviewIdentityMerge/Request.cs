using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.PreviewIdentityMerge;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public Guid OtherId { get; set; }
}
