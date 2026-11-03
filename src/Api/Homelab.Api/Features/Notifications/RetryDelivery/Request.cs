using FastEndpoints;

namespace Homelab.Api.Features.Notifications.RetryDelivery;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }
}
