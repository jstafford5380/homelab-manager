namespace Homelab.Api;

public sealed class ConnectionCredentialHistory
{
    public Guid ConnectionId { get; set; }
    public DateTime ReplacedUtc { get; set; }
}
