
namespace Homelab.Api;
public sealed class DiscoveryAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public Guid DiskId { get; set; }
    public Guid ConfigurationRevision { get; set; }
    public Guid RequestId { get; set; }
    public string SubmissionHash { get; set; } = "";
    public string Wwn { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Model { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public DateTime ValidUntilUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? RevocationEvidence { get; set; }
    public string? RevokedBy { get; set; }
}
