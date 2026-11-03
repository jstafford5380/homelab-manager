using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.PreviewRename;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public long FromSampleId { get; set; }

    [QueryParam]
    public long ToSampleId { get; set; }
}
