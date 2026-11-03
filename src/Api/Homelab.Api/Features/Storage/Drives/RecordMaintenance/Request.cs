using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.RecordMaintenance;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public MaintenanceInput Input { get; set; } = default !;
}
