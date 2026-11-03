using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.PostConnectionsDatasetRenamesById;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public DatasetRenameInput Input { get; set; } = default !;
}
