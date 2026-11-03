
namespace Homelab.Api;
public sealed class DriveMaintenance
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public Guid RequestId { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string DateConfidence { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string ConfirmedIdentity { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public string SourceSchema { get; set; } = "manual-maintenance-v1";
    public DateTime? VoidedUtc { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedBy { get; set; }
}
