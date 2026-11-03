using FastEndpoints;

namespace Homelab.Api.Features.Connections.Collection.GetDailyHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public DateTime? BeforeDay { get; set; }

    [QueryParam]
    public long? BeforeId { get; set; }
}
