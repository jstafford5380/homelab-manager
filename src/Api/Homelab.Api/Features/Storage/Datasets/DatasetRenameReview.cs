
namespace Homelab.Api;
public sealed class DatasetRenameReview
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public long FromSampleId { get; set; }
    public long ToSampleId { get; set; }
    public string FromName { get; set; } = "";
    public string ToName { get; set; } = "";
    public Guid RequestId { get; set; }
    public string SubmissionHash { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public DateTime? ReversedUtc { get; set; }
    public string? ReversalEvidence { get; set; }
    public string? ReversedBy { get; set; }
}
