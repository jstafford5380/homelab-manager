using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.VoidMaintenance;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public long RecordId { get; set; }

    [FromBody]
    public VoidMaintenanceInput Input { get; set; } = default !;
}
