using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.GetTrends;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
