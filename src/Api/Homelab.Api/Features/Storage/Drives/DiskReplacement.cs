
namespace Homelab.Api;
public sealed class DiskReplacement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OldDiskId { get; set; }
    public Guid NewDiskId { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string DateConfidence { get; set; } = "documented";
    public string PoolGuid { get; set; } = "";
    public string MembershipEvidence { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string OldIdentity { get; set; } = "";
    public string NewIdentity { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public DateTime? VoidedUtc { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedBy { get; set; }
}
