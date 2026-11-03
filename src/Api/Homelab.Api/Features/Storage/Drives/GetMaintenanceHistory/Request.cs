using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.GetMaintenanceHistory;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [QueryParam]
    public long? Before { get; set; }

    [QueryParam]
    public int? Days { get; set; }
}
