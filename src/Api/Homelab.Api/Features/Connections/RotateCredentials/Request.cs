using FastEndpoints;

namespace Homelab.Api.Features.Connections.RotateCredentials;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public CredentialRotation Input { get; set; } = default !;
}
