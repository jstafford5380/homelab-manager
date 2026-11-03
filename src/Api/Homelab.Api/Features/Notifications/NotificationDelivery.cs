
namespace Homelab.Api;
public sealed class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelRevision { get; set; }
    public Guid FindingId { get; set; }
    public string EventKey { get; set; } = "";
    public string Kind { get; set; } = "opened";
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedUtc { get; set; }
}
