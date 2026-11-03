using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.ListReports;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
