using FastEndpoints;

namespace Homelab.Api.Features.Settings.UpdateSettings;
public sealed class Request
{
    [FromBody]
    public OperatingSettings Input { get; set; } = default !;
}
