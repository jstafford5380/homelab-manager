using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.ReverseIdentityMerge;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public Guid MergeId { get; set; }

    [FromBody]
    public IdentitySplitInput Input { get; set; } = default !;
}
