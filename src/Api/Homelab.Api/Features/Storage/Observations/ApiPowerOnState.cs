
namespace Homelab.Api;
public class ApiPowerOnState
{
    public Guid ConnectionId { get; set; }
    public DateTime? LastFailedAttemptUtc { get; set; }
    public string SnapshotJson { get; set; } = "";
}
