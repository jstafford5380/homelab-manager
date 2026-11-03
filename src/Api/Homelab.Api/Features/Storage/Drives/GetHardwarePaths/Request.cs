using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetHardwarePaths;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public long? Before { get; set; }
}
