using FastEndpoints;

namespace Homelab.Api.Features.Authentication.SetupOwner;
public sealed class Request
{
    [FromBody]
    public Credentials Input { get; set; } = default !;
}
