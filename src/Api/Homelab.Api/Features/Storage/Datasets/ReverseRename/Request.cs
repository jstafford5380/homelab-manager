using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.ReverseRename;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public long ReviewId { get; set; }

    [FromBody]
    public DatasetRenameReversal Input { get; set; } = default !;
}
