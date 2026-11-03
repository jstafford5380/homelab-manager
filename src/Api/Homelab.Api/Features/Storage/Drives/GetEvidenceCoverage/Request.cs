using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetEvidenceCoverage;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
