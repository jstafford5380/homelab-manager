using FastEndpoints;

namespace Homelab.Api.Features.Storage.Smart.PreviewReport;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public SmartReportInput Input { get; set; } = default !;
}
