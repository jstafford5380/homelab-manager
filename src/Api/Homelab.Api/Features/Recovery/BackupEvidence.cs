
namespace Homelab.Api;
public sealed class BackupEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PolicyId { get; set; }
    public Guid PolicyRevision { get; set; }
    public DateTime AttemptedUtc { get; set; }
    public string Outcome { get; set; } = "unknown";
    public DateTime? RecoveryPointUtc { get; set; }
    public bool DestinationConfirmed { get; set; }
    public string Evidence { get; set; } = "";
    public string FailureReason { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ReviewByUtc { get; set; }
}
