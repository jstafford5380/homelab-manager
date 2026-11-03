
namespace Homelab.Api;
public sealed class CollectionDailySummary
{
    public long Id { get; set; }
    public string GroupKey { get; set; } = "";
    public Guid ConnectionId { get; set; }
    public DateTime DayUtc { get; set; }
    public string Result { get; set; } = "";
    public bool CapabilitiesKnown { get; set; }
    public string CapabilitiesJson { get; set; } = "[]";
    public long AttemptCount { get; set; }
    public DateTime FirstStartedUtc { get; set; }
    public DateTime LastStartedUtc { get; set; }
    public long DurationKnownCount { get; set; }
    public double? DurationMinMs { get; set; }
    public double? DurationMaxMs { get; set; }
}
