using FastEndpoints;

namespace Homelab.Api.Features.Storage.Observations.GetHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public string Metric { get; set; } = default !;

    [QueryParam]
    public int Days { get; set; }

    [QueryParam]
    public DateTime? AsOfUtc { get; set; }

    [QueryParam]
    public long? SnapshotId { get; set; }

    [QueryParam]
    public long? Before { get; set; }
}
