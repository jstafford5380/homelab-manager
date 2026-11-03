using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetReplacements;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
