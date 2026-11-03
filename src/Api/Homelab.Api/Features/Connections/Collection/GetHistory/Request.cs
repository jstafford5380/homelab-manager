using FastEndpoints;

namespace Homelab.Api.Features.Connections.Collection.GetHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public long? Before { get; set; }
}
