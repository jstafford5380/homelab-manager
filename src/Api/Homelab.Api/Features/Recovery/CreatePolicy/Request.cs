using FastEndpoints;

namespace Homelab.Api.Features.Recovery.CreatePolicy;
public sealed class Request
{
    [FromBody]
    public RecoveryPolicy Input { get; set; } = default !;
}
