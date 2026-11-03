using FastEndpoints;

namespace Homelab.Api.Features.Storage.Pools.GetDailyHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public string Guid { get; set; } = default !;

    [QueryParam]
    public DateTime? BeforeDay { get; set; }

    [QueryParam]
    public long? BeforeId { get; set; }
}
