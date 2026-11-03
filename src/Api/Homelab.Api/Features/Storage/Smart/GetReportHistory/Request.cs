using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.GetReportHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public DateTime? AsOf { get; set; }

    [QueryParam]
    public int? Offset { get; set; }
}
