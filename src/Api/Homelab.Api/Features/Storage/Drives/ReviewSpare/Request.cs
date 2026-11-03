using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.ReviewSpare;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public SpareReviewInput Input { get; set; } = default !;
}
