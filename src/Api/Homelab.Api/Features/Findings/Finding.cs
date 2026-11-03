
namespace Homelab.Api;
public sealed class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public Guid? DiskId { get; set; }
    public string Rule { get; set; } = "";
    public string Severity { get; set; } = "review";
    public string Title { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string NextStep { get; set; } = "";
    public string State { get; set; } = "open";
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewUtc { get; set; }
    public string? AssignedOwner { get; set; }
    public DateTime? SnoozedUntilUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
    public string? ResolutionEvidence { get; set; }
    public DateTime? EvidenceObservedUtc { get; set; }
    public string? EvidenceUnit { get; set; }
    public string? EvidenceSource { get; set; }
    public string? EvidenceEpoch { get; set; }
    public string? EvidenceSchema { get; set; }
    public int HealthySamples { get; set; }
    public int ReminderSequence { get; set; }
}
