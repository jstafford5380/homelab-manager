
namespace Homelab.Api;
public sealed class DatasetInventorySample
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid ConfigurationRevision { get; set; }
    public DateTime HourUtc { get; set; }
    public DateTime FetchedUtc { get; set; }
    public string? SystemSerial { get; set; }
    public string? Version { get; set; }
    public string SnapshotJson { get; set; } = "";
}
