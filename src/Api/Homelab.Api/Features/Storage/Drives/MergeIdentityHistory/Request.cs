using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.MergeIdentityHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public IdentityMergeInput Input { get; set; } = default !;
}
