using FastEndpoints;

namespace Homelab.Api.Features.Authentication.SignIn;
public sealed class Request
{
    [FromBody]
    public Credentials Input { get; set; } = default !;
}
