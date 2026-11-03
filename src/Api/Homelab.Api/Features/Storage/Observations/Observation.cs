
namespace Homelab.Api;
public sealed class Observation
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? DiskId { get; set; }
    public string SourceKey { get; set; } = "";
    public string Metric { get; set; } = "";
    public double? Value { get; set; }
    public string Unit { get; set; } = "";
    public DateTime? ObservedUtc { get; set; }
    public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;
    public string? RecordedBy { get; set; }
    public string Source { get; set; } = "manual";
    public string Quality { get; set; } = "unverified";
    public string? MissingReason { get; set; }
    public string ResetEpoch { get; set; } = "";
    public string SourceSchema { get; set; } = "manual-v1";
}
