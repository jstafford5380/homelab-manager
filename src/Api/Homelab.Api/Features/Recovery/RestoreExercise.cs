
namespace Homelab.Api;
public sealed class RestoreExercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PolicyId { get; set; }
    public Guid PolicyRevision { get; set; }
    public DateTime TestedUtc { get; set; }
    public DateTime RecoveryPointUtc { get; set; }
    public string Scope { get; set; } = "";
    public string Checks { get; set; } = "";
    public string Result { get; set; } = "unverified";
    public int DurationMinutes { get; set; }
    public string Evidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ReviewByUtc { get; set; }
}
