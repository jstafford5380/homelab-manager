
namespace Homelab.Api;
public sealed class RecoveryPolicy
{
    public string DestinationCapacity { get; set; } = "";
    public string RestoreRequirements { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Asset { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Mechanism { get; set; } = "independent backup";
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string Owner { get; set; } = "";
    public string Importance { get; set; } = "important";
    public string Scope { get; set; } = "";
    public string Exclusions { get; set; } = "";
    public string Retention { get; set; } = "";
    public string Schedule { get; set; } = "";
    public string FailureDomains { get; set; } = "unknown";
    public string Independence { get; set; } = "unverified";
    public int RtoHours { get; set; } = 4;
    public string Runbook { get; set; } = "";
    public string KeyCustodyReference { get; set; } = "";
    public int RpoHours { get; set; } = 24;
    public DateTime? RecoveryPointUtc { get; set; }
    public DateTime? RestoreTestUtc { get; set; }
    public string RestoreResult { get; set; } = "unverified";
    public string Evidence { get; set; } = "";
}
