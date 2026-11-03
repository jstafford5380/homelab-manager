
namespace Homelab.Api;
public sealed class IdentityHistoryMerge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public Guid FirstDiskId { get; set; }
    public Guid SecondDiskId { get; set; }
    public Guid RequestId { get; set; }
    public string SubmissionHash { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public DateTime? ReversedUtc { get; set; }
    public string? ReversalEvidence { get; set; }
    public string? ReversedBy { get; set; }
}
