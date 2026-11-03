
namespace Homelab.Api;
public sealed class CollectionRun
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public string Result { get; set; } = "";
    public string CapabilitySummaryJson { get; set; } = "{}";
}
