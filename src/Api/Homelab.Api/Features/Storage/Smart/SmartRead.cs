
namespace Homelab.Api;
public sealed class SmartRead
{
    public Guid DiskId { get; set; }
    public DateTime AttemptUtc { get; set; }
    public DateTime? ReadUtc { get; set; }
    public bool SkippedAsleep { get; set; }
    public string? Error { get; set; }
    public string? ReportJson { get; set; }
}
