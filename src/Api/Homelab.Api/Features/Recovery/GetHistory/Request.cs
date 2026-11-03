using FastEndpoints;

namespace Homelab.Api.Features.Recovery.GetHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public string Kind { get; set; } = default !;

    [QueryParam]
    public DateTime? AsOfUtc { get; set; }

    [QueryParam]
    public DateTime? BeforeAtUtc { get; set; }

    [QueryParam]
    public Guid? BeforeId { get; set; }
}
