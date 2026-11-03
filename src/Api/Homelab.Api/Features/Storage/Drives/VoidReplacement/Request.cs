using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.VoidReplacement;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public VoidReplacementInput Input { get; set; } = default !;
}
