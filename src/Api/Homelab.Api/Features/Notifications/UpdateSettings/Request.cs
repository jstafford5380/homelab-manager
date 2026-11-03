using FastEndpoints;

namespace Homelab.Api.Features.Notifications.UpdateSettings;
public sealed class Request
{
    [FromBody]
    public ChannelInput Input { get; set; } = default !;
}
