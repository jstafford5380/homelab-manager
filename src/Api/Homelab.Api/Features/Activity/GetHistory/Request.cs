using FastEndpoints;

namespace Homelab.Api.Features.Activity.GetHistory;
public sealed class Request
{
    [QueryParam]
    public long? SnapshotId { get; set; }

    [QueryParam]
    public long? Before { get; set; }
}
